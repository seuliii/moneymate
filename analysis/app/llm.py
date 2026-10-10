"""Claude 기반 리포트 생성기.

C#이 계산한 통계(facts)만 근거로 구조화 출력(JSON 스키마)으로 리포트를 받는다.
프롬프트를 바꾸면 PROMPT_VERSION과 C# PromptVersion/ModelKey를 함께 바꿔 캐시를 분리한다.
요청 통계와 모델 출력 본문은 로그에 남기지 않는다.
"""

import json
import logging
import time
from typing import Any, List, Optional

import anthropic
from pydantic import BaseModel, ConfigDict, ValidationError

from .contracts import CONTRACT_VERSION, MonthlyReportRequest, MonthlyReportResponse, ReportContent, ReportStatement
from .errors import ServiceError

logger = logging.getLogger("moneymate.analysis")

PROMPT_VERSION = "monthly-v1"
DEFAULT_MODEL = "claude-opus-5-5"
# C#은 내부 요청당 25초 후 끊는다. SDK 재시도 없이 20초 안에 끝내고, 일시 장애는 503으로 알려 C#이 1회 재시도한다.
DEFAULT_TIMEOUT_SECONDS = 20.0
DEFAULT_EFFORT = "low"
MAX_TOKENS = 16000
FALLBACK_BETA = "server-side-fallback-2026-07-01"
TRANSIENT_STATUS = {408, 409, 429}

SYSTEM_PROMPT = """당신은 가계부 서비스 MoneyMate의 월간 지출 리포트 작성자입니다.
사용자 메시지의 JSON은 서버가 계산을 마친 한 달 통계입니다. 이 통계만 근거로 한국어 리포트를 작성합니다.

작성 규칙:
- 모든 수치·기간은 facts와 categories 값을 그대로 씁니다. 새로 계산하거나 반올림을 바꾸지 않습니다. 금액은 "154,500원"처럼 씁니다.
- 각 문장의 evidence_ids에는 그 문장이 인용한 facts의 id만 넣습니다. citableFactIds 목록에 없는 id(값이 null인 fact 포함)는 쓰지 않습니다.
- null 값은 비교할 수 없다는 뜻입니다. 0으로 해석하거나 증감을 말하지 않습니다.
- 전월과 비교할 때는 comparisonLabel의 표현(예: "전월" 또는 "전월 같은 기간")을 그대로 씁니다. comparison.status가 comparable이 아니면 증감을 말하지 않습니다.
- 사용자 성향, 예산 초과, 제공되지 않은 장기 추세나 원인은 추정하지 않습니다. "낭비", "과소비" 같은 단정·평가 표현을 쓰지 않습니다.
- 수지(net)는 실제 잔액이나 저축액이 아닙니다. 기록이 완전하다고 가정하지 않습니다.
- summary는 한 문장, highlights·changes_to_review·suggestions는 각각 0~3개입니다. 말할 근거가 없으면 빈 배열로 둡니다.
- 각 text는 500자 이내의 짧은 문장으로 씁니다.

섹션:
- summary: 대상 월 지출 총액과(비교 가능하면) 비교 기준 대비 증감.
- highlights: 지출이 큰 카테고리와 비중 등 눈에 띄는 사실.
- changes_to_review: 비교 기준보다 늘어난 카테고리 등 확인해볼 만한 변화. 단정하지 말고 확인을 권합니다.
- suggestions: 기록·확인 습관 수준의 가벼운 제안. 근거가 된 fact를 인용합니다."""


class _Statement(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str
    evidence_ids: List[str]


# 모델 출력 스키마. 개수·길이·근거 ID 제한은 main의 validate_response가 다시 확인한다.
class _Report(BaseModel):
    model_config = ConfigDict(extra="forbid")
    summary: _Statement
    highlights: List[_Statement]
    changes_to_review: List[_Statement]
    suggestions: List[_Statement]


OUTPUT_FORMAT = {"type": "json_schema", "schema": anthropic.transform_schema(_Report)}


def _comparison_label(request: MonthlyReportRequest) -> str:
    return "전월 같은 기간" if request.comparison.mode == "same_day" else "전월"


def build_user_message(request: MonthlyReportRequest) -> str:
    # requestId·dataVersion은 문장 작성에 필요 없어 보내지 않는다. 계약상 개인 식별 정보는 애초에 없다.
    payload = request.model_dump(
        mode="json",
        by_alias=True,
        include={
            "month", "as_of_date", "is_calendar_month_complete", "period", "record_count",
            "facts", "categories", "comparison", "largest_expenses", "limitations",
        },
    )
    payload["comparisonLabel"] = _comparison_label(request)
    payload["citableFactIds"] = sorted(request.citable_fact_ids())
    return json.dumps(payload, ensure_ascii=False, sort_keys=True)


def _statement(item: _Statement) -> ReportStatement:
    return ReportStatement(text=item.text.strip(), evidence_ids=item.evidence_ids)


class LlmReportGenerator:
    def __init__(
        self,
        client: Optional[Any] = None,
        model: str = DEFAULT_MODEL,
        effort: str = DEFAULT_EFFORT,
        timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
    ) -> None:
        self._client = client or anthropic.Anthropic(timeout=timeout_seconds, max_retries=0)
        self._model = model
        self._effort = effort

    def generate(self, request: MonthlyReportRequest) -> MonthlyReportResponse:
        started = time.monotonic()
        try:
            response = self._client.beta.messages.create(
                model=self._model,
                max_tokens=MAX_TOKENS,
                system=SYSTEM_PROMPT,
                messages=[{"role": "user", "content": build_user_message(request)}],
                output_config={"effort": self._effort, "format": OUTPUT_FORMAT},
                betas=[FALLBACK_BETA],
                fallbacks="default",
            )
        except anthropic.APIConnectionError as error:
            # 연결 실패·시간 초과(APITimeoutError 포함)는 일시 장애로 보고 C# 재시도에 맡긴다.
            self._log_failure(request, started, error)
            raise ServiceError(503, "llm_unavailable")
        except anthropic.APIStatusError as error:
            self._log_failure(request, started, error)
            # SDK 재시도 대상과 같은 408/409/429/5xx(529 과부하 포함)만 일시 장애. 400/401/403/404 등은 재시도해도 같다.
            if error.status_code in TRANSIENT_STATUS or error.status_code >= 500:
                raise ServiceError(503, "llm_unavailable")
            raise ServiceError(500, "llm_request_failed")

        usage = response.usage
        logger.info(
            "llm report: requestId=%s promptVersion=%s model=%s stopReason=%s inputTokens=%s outputTokens=%s latencyMs=%d",
            request.request_id, PROMPT_VERSION, response.model, response.stop_reason,
            usage.input_tokens, usage.output_tokens, (time.monotonic() - started) * 1000,
        )
        if response.stop_reason == "refusal":
            raise ServiceError(500, "llm_refused")
        if response.stop_reason != "end_turn":
            raise ServiceError(500, "llm_invalid_output")
        text = next((block.text for block in response.content if block.type == "text"), "")
        try:
            report = _Report.model_validate_json(text)
        except ValidationError:
            raise ServiceError(500, "llm_invalid_output")

        content = ReportContent(
            summary=_statement(report.summary),
            highlights=[_statement(item) for item in report.highlights],
            changes_to_review=[_statement(item) for item in report.changes_to_review],
            suggestions=[_statement(item) for item in report.suggestions],
        )
        return MonthlyReportResponse(contract_version=CONTRACT_VERSION, request_id=request.request_id, report=content)

    def _log_failure(self, request: MonthlyReportRequest, started: float, error: Exception) -> None:
        # 예외 메시지에는 API 응답 본문이 담길 수 있어 유형·상태 코드만 남긴다.
        logger.warning(
            "llm call failed: requestId=%s model=%s type=%s status=%s latencyMs=%d",
            request.request_id, self._model, type(error).__name__,
            getattr(error, "status_code", None), (time.monotonic() - started) * 1000,
        )
