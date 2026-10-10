"""LLM 생성기 공통: 프롬프트(monthly-v1), 출력 스키마, 응답 변환.

프롬프트를 바꾸면 PROMPT_VERSION과 C# PromptVersion/ModelKey를 함께 바꿔 캐시를 분리한다.
"""

import json
from decimal import Decimal
from typing import List, Optional, Union

from pydantic import BaseModel, ConfigDict, ValidationError

from .contracts import CONTRACT_VERSION, MonthlyReportRequest, MonthlyReportResponse, ReportContent, ReportStatement
from .errors import ServiceError

PROMPT_VERSION = "monthly-v1"
# C#은 내부 요청당 25초 후 끊는다. 생성기는 재시도 없이 20초 안에 끝내고, 일시 장애는 503으로 알려 C#이 1회 재시도한다.
DEFAULT_TIMEOUT_SECONDS = 20.0

SYSTEM_PROMPT = """당신은 가계부 서비스 MoneyMate의 월간 지출 리포트 작성자입니다.
사용자 메시지의 JSON은 서버가 계산을 마친 한 달 통계입니다. 이 통계만 근거로 한국어 리포트를 작성합니다.

작성 규칙:
- 모든 수치·기간은 facts와 categories 값을 그대로 씁니다. 새로 계산하거나 반올림을 바꾸지 않습니다. 금액은 "154,500원"처럼 씁니다.
- facts는 {id: 값} 형태입니다. id가 .share/.rate로 끝나거나 comparison.rate이면 단위는 %, 나머지는 원입니다.
- 각 문장의 evidence_ids에는 그 문장이 인용한 facts의 id만 넣습니다. citableFactIds 목록에 없는 id(값이 null인 fact 포함)는 쓰지 않습니다.
- null 값은 비교할 수 없다는 뜻입니다. 0으로 해석하거나 증감을 말하지 않습니다.
- comparison.status가 comparable일 때만 증감을 말하고, 이때 comparisonLabel의 표현(예: "전월" 또는 "전월 같은 기간")을 그대로 씁니다. comparable이 아니면 전월 금액·증감·증감률을 말하지 않습니다.
- 사용자 성향, 예산, 제공되지 않은 장기 추세나 원인은 추정하거나 언급하지 않습니다. "낭비", "과소비", "주의" 같은 평가·경고 표현을 쓰지 않습니다.
- 수지(net)는 실제 잔액이나 저축액이 아닙니다. 기록이 완전하다고 가정하지 않습니다.
- summary는 한 문장, highlights·changes_to_review·suggestions는 각각 0~2개입니다. 말할 근거가 없으면 빈 배열로 둡니다.
- 모든 text는 100자 안팎의 짧은 한 문장으로 씁니다.

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
class ReportOutput(BaseModel):
    model_config = ConfigDict(extra="forbid")
    summary: _Statement
    highlights: List[_Statement]
    changes_to_review: List[_Statement]
    suggestions: List[_Statement]


def _comparison_label(request: MonthlyReportRequest) -> str:
    return "전월 같은 기간" if request.comparison.mode == "same_day" else "전월"


def _number(value: Optional[Decimal]) -> Union[int, float, None]:
    if value is None:
        return None
    return int(value) if value == value.to_integral_value() else float(value)


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
    # facts는 {id: 값}으로 줄여 입력 토큰을 아낀다(로컬 모델은 입력 처리도 지연에 크게 영향).
    payload["facts"] = {fact.id: _number(fact.value) for fact in request.facts}
    payload["comparisonLabel"] = _comparison_label(request)
    payload["citableFactIds"] = sorted(request.citable_fact_ids())
    return json.dumps(payload, ensure_ascii=False, sort_keys=True)


def _statement(item: _Statement) -> ReportStatement:
    return ReportStatement(text=item.text.strip(), evidence_ids=item.evidence_ids)


def parse_report(request: MonthlyReportRequest, text: str) -> MonthlyReportResponse:
    """모델이 낸 JSON 문자열을 계약 응답으로 바꾼다. 스키마에 맞지 않으면 500(llm_invalid_output)."""
    try:
        report = ReportOutput.model_validate_json(text)
    except ValidationError:
        raise ServiceError(500, "llm_invalid_output")
    content = ReportContent(
        summary=_statement(report.summary),
        highlights=[_statement(item) for item in report.highlights],
        changes_to_review=[_statement(item) for item in report.changes_to_review],
        suggestions=[_statement(item) for item in report.suggestions],
    )
    return MonthlyReportResponse(contract_version=CONTRACT_VERSION, request_id=request.request_id, report=content)
