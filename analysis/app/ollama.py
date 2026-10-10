"""Ollama(로컬 오픈 모델) 기반 리포트 생성기.

무료로 로컬에서 실행하며 통계가 PC/서버 밖으로 나가지 않는다.
Ollama의 JSON 스키마 출력(format)으로 리포트를 받고, 프롬프트는 Claude 생성기와 같다.
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

DEFAULT_URL = "http://127.0.0.1:11434"
DEFAULT_MODEL = "qwen2.5:7b"
# 통계 JSON과 프롬프트가 기본 컨텍스트(4096)를 넘지 않도록 넉넉히 잡는다.
NUM_CTX = 8192
# 모델을 메모리에 유지해 요청마다 다시 올리는 지연(수 초)을 피한다.
KEEP_ALIVE = "30m"
OUTPUT_SCHEMA = ReportOutput.model_json_schema()


class OllamaReportGenerator:
    def __init__(
        self,
        base_url: str = DEFAULT_URL,
        model: str = DEFAULT_MODEL,
        timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
        client: Optional[httpx.Client] = None,
    ) -> None:
        self._url = base_url.rstrip("/") + "/api/chat"
        self._model = model
        self._client = client or httpx.Client(timeout=timeout_seconds)

    def generate(self, request: MonthlyReportRequest) -> MonthlyReportResponse:
        started = time.monotonic()
        body = {
            "model": self._model,
            "messages": [
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": build_user_message(request)},
            ],
            "format": OUTPUT_SCHEMA,
            "stream": False,
            "keep_alive": KEEP_ALIVE,
            # 같은 통계에 같은 리포트가 나오도록 무작위성을 끈다.
            "options": {"temperature": 0, "num_ctx": NUM_CTX},
        }
        try:
            response = self._client.post(self._url, json=body)
        except httpx.TransportError as error:
            # 연결 실패·시간 초과는 일시 장애로 보고 C# 재시도에 맡긴다.
            self._log_failure(request, started, type(error).__name__, None)
            raise ServiceError(503, "llm_unavailable")
        if response.status_code != 200:
            self._log_failure(request, started, "HTTPStatus", response.status_code)
            # 5xx(모델 로딩 실패·과부하 등)는 일시 장애. 404(모델 미설치)·400은 설정 문제라 재시도해도 같다.
            if response.status_code >= 500:
                raise ServiceError(503, "llm_unavailable")
            raise ServiceError(500, "llm_request_failed")

        try:
            result = response.json()
        except ValueError:
            raise ServiceError(500, "llm_invalid_output")
        logger.info(
            "ollama report: requestId=%s promptVersion=%s model=%s doneReason=%s inputTokens=%s outputTokens=%s latencyMs=%d",
            request.request_id, PROMPT_VERSION, self._model, result.get("done_reason"),
            result.get("prompt_eval_count"), result.get("eval_count"), (time.monotonic() - started) * 1000,
        )
        if result.get("done_reason") != "stop":
            # length: 출력이 잘림. 잘린 JSON은 쓰지 않는다.
            raise ServiceError(500, "llm_invalid_output")
        return parse_report(request, (result.get("message") or {}).get("content", ""))

    def _log_failure(self, request: MonthlyReportRequest, started: float, kind: str, status: Optional[int]) -> None:
        # 예외 메시지·응답 본문은 남기지 않고 유형·상태 코드만 기록한다.
        logger.warning(
            "ollama call failed: requestId=%s model=%s type=%s status=%s latencyMs=%d",
            request.request_id, self._model, kind, status, (time.monotonic() - started) * 1000,
        )
