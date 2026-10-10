"""Claude(Anthropic API) 기반 리포트 생성기.

C#이 계산한 통계(facts)만 근거로 구조화 출력(JSON 스키마)으로 리포트를 받는다.
요청 통계와 모델 출력 본문은 로그에 남기지 않는다.
"""

import logging
import time
from typing import Any, Optional

import anthropic

from .contracts import MonthlyReportRequest, MonthlyReportResponse
from .errors import ServiceError
from .prompt import DEFAULT_TIMEOUT_SECONDS, PROMPT_VERSION, SYSTEM_PROMPT, ReportOutput, build_user_message, parse_report

logger = logging.getLogger("moneymate.analysis")

DEFAULT_MODEL = "claude-opus-5-5"
DEFAULT_EFFORT = "low"
MAX_TOKENS = 16000
FALLBACK_BETA = "server-side-fallback-2026-07-01"
TRANSIENT_STATUS = {408, 409, 429}

OUTPUT_FORMAT = {"type": "json_schema", "schema": anthropic.transform_schema(ReportOutput)}


class ClaudeReportGenerator:
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
            "claude report: requestId=%s promptVersion=%s model=%s stopReason=%s inputTokens=%s outputTokens=%s latencyMs=%d",
            request.request_id, PROMPT_VERSION, response.model, response.stop_reason,
            usage.input_tokens, usage.output_tokens, (time.monotonic() - started) * 1000,
        )
        if response.stop_reason == "refusal":
            raise ServiceError(500, "llm_refused")
        if response.stop_reason != "end_turn":
            raise ServiceError(500, "llm_invalid_output")
        text = next((block.text for block in response.content if block.type == "text"), "")
        return parse_report(request, text)

    def _log_failure(self, request: MonthlyReportRequest, started: float, error: Exception) -> None:
        # 예외 메시지에는 API 응답 본문이 담길 수 있어 유형·상태 코드만 남긴다.
        logger.warning(
            "claude call failed: requestId=%s model=%s type=%s status=%s latencyMs=%d",
            request.request_id, self._model, type(error).__name__,
            getattr(error, "status_code", None), (time.monotonic() - started) * 1000,
        )
