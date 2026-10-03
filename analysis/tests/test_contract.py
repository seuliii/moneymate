import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.contracts import MonthlyReportRequest, MonthlyReportResponse
from app.generators import StubReportGenerator
from app.main import ServiceError, create_app
from app.validation import validate_response

EXAMPLES = Path(__file__).resolve().parents[2] / "docs" / "api" / "examples"
KEY = "fictional-test-key"
PATH = "/internal/v1/reports/monthly"
AUTH = {"Authorization": f"Bearer {KEY}"}


def load(name: str) -> dict:
    return json.loads((EXAMPLES / name).read_text(encoding="utf-8"))


@pytest.fixture
def request_json() -> dict:
    return load("monthly-request.json")


@pytest.fixture
def client() -> TestClient:
    return TestClient(create_app(service_key=KEY))


# --- 저장소 예제와 계약 모델 ---


def test_examples_parse_and_validate(request_json):
    request = MonthlyReportRequest.model_validate(request_json)
    response = MonthlyReportResponse.model_validate(load("monthly-response.json"))
    assert len(request.facts) == 23
    assert validate_response(request, response) == []


def test_request_rejects_unknown_field_and_version(request_json):
    with pytest.raises(ValueError):
        MonthlyReportRequest.model_validate({**request_json, "userId": "x"})
    with pytest.raises(ValueError):
        MonthlyReportRequest.model_validate({**request_json, "contractVersion": "2.0"})


def test_request_accepts_null_comparison(request_json):
    request_json["comparison"] = {"status": "no_previous_records", "mode": "same_day", "previousPeriod": None, "differentDayCounts": False}
    for fact in request_json["facts"]:
        if fact["id"].startswith("comparison.") or fact["id"].endswith((".delta", ".rate")):
            fact["value"] = None
    request = MonthlyReportRequest.model_validate(request_json)
    assert "comparison.delta" not in request.citable_fact_ids()


# --- 응답 검증 (C# AnalysisContract.Valid 기준) ---


def _with_summary(response: MonthlyReportResponse, text: str, ids) -> MonthlyReportResponse:
    summary = response.report.summary.model_copy(update={"text": text, "evidence_ids": ids})
    return response.model_copy(update={"report": response.report.model_copy(update={"summary": summary})})


def test_validation_rejects_contract_violations(request_json):
    request = MonthlyReportRequest.model_validate(request_json)
    response = MonthlyReportResponse.model_validate(load("monthly-response.json"))
    assert validate_response(request, _with_summary(response, "x", ["not.in.facts"]))
    assert validate_response(request, _with_summary(response, "x" * 501, ["expense.total"]))
    assert validate_response(request, _with_summary(response, "😀" * 251, ["expense.total"])), "UTF-16 길이 기준"
    assert validate_response(request, _with_summary(response, "   ", ["expense.total"]))
    assert validate_response(request, _with_summary(response, "x", []))
    four = [response.report.summary] * 4
    assert validate_response(request, response.model_copy(update={"report": response.report.model_copy(update={"suggestions": four})}))

    request_json["facts"][1]["value"] = None  # expense.total
    assert validate_response(MonthlyReportRequest.model_validate(request_json), response), "null 근거 인용 금지"


# --- HTTP 엔드포인트 ---


def test_requires_service_key(client, request_json):
    assert client.post(PATH, json=request_json).status_code == 401
    assert client.post(PATH, json=request_json, headers={"Authorization": "Bearer wrong"}).status_code == 401
    # 인증 실패가 입력 검증보다 먼저 판단된다.
    assert client.post(PATH, json={}, headers={"Authorization": "Bearer wrong"}).status_code == 401


def test_invalid_request_is_400(client, request_json):
    response = client.post(PATH, json={**request_json, "title": "점심"}, headers=AUTH)
    assert response.status_code == 400
    assert response.json() == {"code": "invalid_request"}


def test_stub_report_matches_contract(client, request_json):
    response = client.post(PATH, json=request_json, headers=AUTH)
    assert response.status_code == 200
    body = response.json()
    assert set(body) == {"contractVersion", "requestId", "report"}
    assert set(body["report"]) == {"summary", "highlights", "changesToReview", "suggestions"}
    assert body["requestId"] == request_json["requestId"]
    request = MonthlyReportRequest.model_validate(request_json)
    assert validate_response(request, MonthlyReportResponse.model_validate(body), response.content) == []
    assert body["report"]["summary"]["text"] == "등록된 9월 지출은 154,500원으로 전월보다 34,500원 증가했습니다."


def test_stub_handles_no_previous_records_and_no_expense(request_json):
    request_json["comparison"]["status"] = "no_previous_records"
    for fact in request_json["facts"]:
        if fact["id"] == "expense.total":
            fact["value"] = 0
        elif fact["id"].startswith("comparison.") or fact["id"].endswith((".delta", ".rate", ".share")):
            fact["value"] = None
    request_json["categories"] = []
    request = MonthlyReportRequest.model_validate(request_json)
    response = StubReportGenerator().generate(request)
    assert validate_response(request, response) == []
    assert response.report.changes_to_review == []


def test_generator_failures_map_to_status(request_json):
    class Broken:
        def generate(self, request):
            raise RuntimeError("boom")

    class Unavailable:
        def generate(self, request):
            raise ServiceError(503, "llm_unavailable")

    class Invalid:
        def generate(self, request):
            response = MonthlyReportResponse.model_validate(load("monthly-response.json"))
            return _with_summary(response, "x", ["not.in.facts"])

    for generator, status in ((Broken(), 500), (Unavailable(), 503), (Invalid(), 500)):
        client = TestClient(create_app(service_key=KEY, generator=generator), raise_server_exceptions=False)
        assert client.post(PATH, json=request_json, headers=AUTH).status_code == status


def test_missing_service_key_fails_fast(monkeypatch):
    monkeypatch.delenv("ANALYSIS_SERVICE_KEY", raising=False)
    with pytest.raises(RuntimeError):
        create_app()
