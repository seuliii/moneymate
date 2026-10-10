"""MoneyMate 파트너 분석 API.

실행: uvicorn app.main:create_app --factory --host 127.0.0.1 --port 5090
서비스 키는 환경변수 ANALYSIS_SERVICE_KEY로만 주입한다.
ANALYSIS_GENERATOR로 생성기를 고른다: stub(기본) | ollama | claude. 모델은 ANALYSIS_LLM_MODEL.
"""

import hmac
import logging
import os
from typing import Optional

from fastapi import Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, Response
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from .contracts import MonthlyReportRequest
from .errors import ServiceError
from .generators import ReportGenerator, StubReportGenerator
from .validation import validate_response

logger = logging.getLogger("moneymate.analysis")

__all__ = ["ServiceError", "create_app"]


def _generator_from_env() -> ReportGenerator:
    kind = os.environ.get("ANALYSIS_GENERATOR", "stub")
    model = os.environ.get("ANALYSIS_LLM_MODEL")
    if kind == "stub":
        return StubReportGenerator()
    if kind == "ollama":
        from .ollama import DEFAULT_MODEL, DEFAULT_URL, OllamaReportGenerator

        return OllamaReportGenerator(base_url=os.environ.get("ANALYSIS_OLLAMA_URL", DEFAULT_URL), model=model or DEFAULT_MODEL)
    if kind == "claude":
        # anthropic SDK는 이 모드에서만 불러온다. 자격 증명은 SDK 기본값(ANTHROPIC_API_KEY 등)을 따른다.
        from .claude import DEFAULT_EFFORT, DEFAULT_MODEL, ClaudeReportGenerator

        return ClaudeReportGenerator(model=model or DEFAULT_MODEL, effort=os.environ.get("ANALYSIS_LLM_EFFORT", DEFAULT_EFFORT))
    raise RuntimeError("ANALYSIS_GENERATOR는 stub, ollama, claude 중 하나여야 합니다.")


def create_app(service_key: Optional[str] = None, generator: Optional[ReportGenerator] = None) -> FastAPI:
    key = service_key if service_key is not None else os.environ.get("ANALYSIS_SERVICE_KEY", "")
    if not key:
        raise RuntimeError("ANALYSIS_SERVICE_KEY 환경변수가 필요합니다.")
    report_generator = generator or _generator_from_env()
    bearer = HTTPBearer(auto_error=False)

    app = FastAPI(title="MoneyMate Analysis", version="1.0")

    def authorize(credentials: Optional[HTTPAuthorizationCredentials] = Depends(bearer)) -> None:
        if credentials is None or not hmac.compare_digest(credentials.credentials.encode(), key.encode()):
            raise ServiceError(401, "unauthorized")

    @app.exception_handler(ServiceError)
    async def service_error(_: Request, error: ServiceError) -> JSONResponse:
        return JSONResponse(status_code=error.status, content={"code": error.code})

    @app.exception_handler(RequestValidationError)
    async def invalid_request(_: Request, error: RequestValidationError) -> JSONResponse:
        # 계약상 입력 오류는 400. 요청 값(통계)은 로그·응답에 남기지 않고 위치만 기록한다.
        logger.warning("invalid request: %s", [".".join(map(str, e["loc"])) for e in error.errors()])
        return JSONResponse(status_code=400, content={"code": "invalid_request"})

    @app.post("/internal/v1/reports/monthly", dependencies=[Depends(authorize)])
    def monthly_report(body: MonthlyReportRequest) -> Response:
        try:
            result = report_generator.generate(body)
        except ServiceError:
            raise
        except Exception as error:
            # 예외 메시지·스택에는 요청 통계나 LLM 응답이 섞일 수 있어 유형과 코드만 기록한다.
            logger.error(
                "report generation failed: requestId=%s code=generation_failed type=%s",
                body.request_id,
                type(error).__name__,
            )
            # 일시 장애(503, C# 1회 재시도)는 생성기가 ServiceError로 직접 알린다.
            raise ServiceError(500, "generation_failed")
        payload = result.model_dump_json(by_alias=True).encode()
        errors = validate_response(body, result, payload)
        if errors:
            logger.error("invalid report output: requestId=%s code=invalid_report_output errors=%s", body.request_id, errors)
            raise ServiceError(500, "invalid_report_output")
        return Response(content=payload, media_type="application/json")

    return app
