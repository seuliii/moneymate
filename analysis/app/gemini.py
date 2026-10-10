"""Gemini API(Google) 기반 리포트 생성기.

generateContent REST API의 JSON 스키마 출력으로 리포트를 받고, 프롬프트는 다른 LLM 생성기와 같다.
무료 티어는 입력이 Google 제품 개선에 쓰일 수 있다. 계약상 개인 식별 정보는 보내지 않는다.
API 키는 GEMINI_API_KEY 환경변수로만 받고 헤더로 보낸다(URL·로그에 남기지 않음).
요청 통계와 모델 출력 본문은 로그에 남기지 않는다.
"""

import logging
import time
from typing import Optional

import httpx

from .contracts import MonthlyReportRequest, MonthlyReportResponse
from .errors import ServiceError
from .prompt import DEFAULT_TIMEOUT_SECONDS, PROMPT_VERSION, SYSTEM_PROMPT, ReportOutput, build_user_message, parse_report

logger = logging.getLogger("moneymate.analysis")

BASE_URL = "https://generativelanguage.googleapis.com/v1beta"
DEFAULT_MODEL = "gemini-3.5-flash-lite"
DEFAULT_THINKING_LEVEL = "low"
OUTPUT_SCHEMA = ReportOutput.model_json_schema()
# 안전 필터 등으로 응답이 막힌 종료 사유. 재시도해도 같으므로 500(llm_refused).
BLOCKED_REASONS = {"SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII"}


class GeminiReportGenerator:
    def __init__(
        self,
        api_key: str,
        model: str = DEFAULT_MODEL,
        thinking_level: str = DEFAULT_THINKING_LEVEL,
        timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
        base_url: str = BASE_URL,
        client: Optional[httpx.Client] = None,
    ) -> None:
        if not api_key:
            raise RuntimeError("GEMINI_API_KEY 환경변수가 필요합니다.")
        self._url = f"{base_url.rstrip('/')}/models/{model}:generateContent"
        self._headers = {"x-goog-api-key": api_key}
        self._model = model
        self._thinking_level = thinking_level
        self._client = client or httpx.Client(timeout=timeout_seconds)

    def generate(self, request: MonthlyReportRequest) -> MonthlyReportResponse:
        started = time.monotonic()
        body = {
            "systemInstruction": {"parts": [{"text": SYSTEM_PROMPT}]},
            "contents": [{"role": "user", "parts": [{"text": build_user_message(request)}]}],
            "generationConfig": {
                # 같은 통계에 최대한 같은 리포트가 나오도록 무작위성을 끈다.
                "temperature": 0,
                "responseMimeType": "application/json",
                "responseJsonSchema": OUTPUT_SCHEMA,
                # 생각(thinking)은 지연을 늘리므로 낮게 둔다. 통계 서술에는 깊은 추론이 필요 없다.
                "thinkingConfig": {"thinkingLevel": self._thinking_level},
            },
        }
        try:
            response = self._client.post(self._url, json=body, headers=self._headers)
        except httpx.TransportError as error:
            # 연결 실패·시간 초과는 일시 장애로 보고 C# 재시도에 맡긴다.
            self._log_failure(request, started, type(error).__name__, None)
            raise ServiceError(503, "llm_unavailable")
        if response.status_code != 200:
            self._log_failure(request, started, "HTTPStatus", response.status_code)
            # 429(무료 티어 분당 한도 등)·5xx는 일시 장애. 400/401/403/404(키·모델·요청 문제)는 재시도해도 같다.
            if response.status_code == 429 or response.status_code >= 500:
                raise ServiceError(503, "llm_unavailable")
            raise ServiceError(500, "llm_request_failed")

        try:
            result = response.json()
        except ValueError:
            raise ServiceError(500, "llm_invalid_output")
        candidate = (result.get("candidates") or [{}])[0]
        finish_reason = candidate.get("finishReason")
        usage = result.get("usageMetadata") or {}
        logger.info(
            "gemini report: requestId=%s promptVersion=%s model=%s finishReason=%s inputTokens=%s outputTokens=%s thoughtTokens=%s latencyMs=%d",
            request.request_id, PROMPT_VERSION, self._model, finish_reason, usage.get("promptTokenCount"),
            usage.get("candidatesTokenCount"), usage.get("thoughtsTokenCount"), (time.monotonic() - started) * 1000,
        )
        if (result.get("promptFeedback") or {}).get("blockReason") or finish_reason in BLOCKED_REASONS:
            raise ServiceError(500, "llm_refused")
        if finish_reason != "STOP":
            # MAX_TOKENS 등: 잘린 JSON은 쓰지 않는다.
            raise ServiceError(500, "llm_invalid_output")
        parts = (candidate.get("content") or {}).get("parts") or []
        # 생각 요약 파트(thought=true)는 제외하고 본문 텍스트만 합친다.
        text = "".join(part.get("text", "") for part in parts if not part.get("thought"))
        return parse_report(request, text)

    def _log_failure(self, request: MonthlyReportRequest, started: float, kind: str, status: Optional[int]) -> None:
        # 예외 메시지·응답 본문은 남기지 않고 유형·상태 코드만 기록한다.
        logger.warning(
            "gemini call failed: requestId=%s model=%s type=%s status=%s latencyMs=%d",
            request.request_id, self._model, kind, status, (time.monotonic() - started) * 1000,
        )

