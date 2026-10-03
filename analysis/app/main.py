"""MoneyMate 파트너 분석 API.

실행: uvicorn app.main:create_app --factory --host 127.0.0.1 --port 5090
서비스 키는 환경변수 ANALYSIS_SERVICE_KEY로만 주입한다.
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
from .generators import ReportGenerator, StubReportGenerator
from .validation import validate_response

logger = logging.getLogger("moneymate.analysis")


class ServiceError(Exception):
    def __init__(self, status: int, code: str) -> None:
        self.status = status
        self.code = code


def create_app(service_key: Optional[str] = None, generator: Optional[ReportGenerator] = None) -> FastAPI:
    key = service_key if service_key is not None else os.environ.get("ANALYSIS_SERVICE_KEY", "")
    if not key:
        raise RuntimeError("ANALYSIS_SERVICE_KEY 환경변수가 필요합니다.")
    report_generator = generator or StubReportGenerator()
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
        except Exception:
            logger.exception("report generation failed: requestId=%s", body.request_id)
            # 일시 장애(503, C# 1회 재시도)는 생성기가 ServiceError로 직접 알린다.
            raise ServiceError(500, "generation_failed")
        payload = result.model_dump_json(by_alias=True).encode()
        errors = validate_response(body, result, payload)
        if errors:
            logger.error("invalid report output: requestId=%s errors=%s", body.request_id, errors)
            raise ServiceError(500, "invalid_report_output")
        return Response(content=payload, media_type="application/json")

    return app
