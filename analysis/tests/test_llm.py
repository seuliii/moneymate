import json
from pathlib import Path

import anthropic
import httpx
import pytest
from fastapi.testclient import TestClient

from app.contracts import MonthlyReportRequest
from app.errors import ServiceError
from app.llm import FALLBACK_BETA, PROMPT_VERSION, LlmReportGenerator, build_user_message
from app.main import create_app
from app.validation import validate_response

EXAMPLES = Path(__file__).resolve().parents[2] / "docs" / "api" / "examples"
KEY = "fictional-test-key"
PATH = "/internal/v1/reports/monthly"
AUTH = {"Authorization": f"Bearer {KEY}"}

REPORT = {
    "summary": {"text": "등록된 9월 지출은 154,500원으로 전월보다 34,500원 증가했습니다.", "evidence_ids": ["expense.total", "comparison.delta"]},
    "highlights": [{"text": "쇼핑 지출은 120,000원으로 전체 지출의 77.67%입니다.", "evidence_ids": ["category.expense_shopping.amount", "category.expense_shopping.share"]}],
    "changes_to_review": [{"text": "쇼핑 지출이 전월보다 30,000원 증가했습니다. 일회성 지출인지 확인해볼 수 있습니다.", "evidence_ids": ["category.expense_shopping.delta"]}],
    "suggestions": [],
}


@pytest.fixture
def request_json() -> dict:
    return json.loads((EXAMPLES / "monthly-request.json").read_text(encoding="utf-8"))


@pytest.fixture
def request_model(request_json) -> MonthlyReportRequest:
    return MonthlyReportRequest.model_validate(request_json)


def message_body(report=REPORT, stop_reason="end_turn") -> dict:
    return {
        "id": "msg_test",
        "type": "message",
        "role": "assistant",
        "model": "claude-opus-5-5",
        "content": [{"type": "text", "text": json.dumps(report, ensure_ascii=False)}] if report is not None else [],
        "stop_reason": stop_reason,
        "stop_sequence": None,
        "usage": {"input_tokens": 1200, "output_tokens": 300},
    }


def sdk_client(handler) -> anthropic.Anthropic:
    """실제 SDK 직렬화를 거치되 네트워크 대신 handler가 응답한다."""
    return anthropic.Anthropic(
        api_key="fictional-anthropic-key", max_retries=0, http_client=httpx.Client(transport=httpx.MockTransport(handler))
    )


def test_generates_contract_valid_report_and_sends_expected_request(request_model):
    captured = {}

    def handler(http_request: httpx.Request) -> httpx.Response:
        captured["headers"] = http_request.headers
        captured["body"] = json.loads(http_request.content)
        return httpx.Response(200, json=message_body())

    response = LlmReportGenerator(client=sdk_client(handler)).generate(request_model)

    assert validate_response(request_model, response) == []
    assert response.request_id == request_model.request_id
    assert response.report.summary.text.startswith("등록된 9월 지출은 154,500원")

    body = captured["body"]
    assert body["model"] == "claude-opus-5-5"
    assert body["fallbacks"] == "default"
    assert FALLBACK_BETA in captured["headers"]["anthropic-beta"]
    assert body["output_config"]["effort"] == "low"
    schema = body["output_config"]["format"]["schema"]
    assert set(schema["required"]) == {"summary", "highlights", "changes_to_review", "suggestions"}
    assert "thinking" not in body  # Opus 5.5는 thinking을 끌 수 없어 effort로만 조절한다.


def test_user_message_excludes_identifiers_and_lists_citable_ids(request_model, request_json):
    request_json["facts"][1]["value"] = None  # expense.total
    payload = json.loads(build_user_message(MonthlyReportRequest.model_validate(request_json)))
    assert "requestId" not in payload and "dataVersion" not in payload
    assert payload["comparisonLabel"] == "전월"
    assert "expense.total" not in payload["citableFactIds"]
    assert "comparison.delta" in payload["citableFactIds"]


@pytest.mark.parametrize(
    "status, expected",
    [(429, 503), (500, 503), (529, 503), (408, 503), (400, 500), (401, 500), (404, 500)],
)
def test_api_errors_map_to_service_status(request_model, status, expected):
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(status, json={"type": "error", "error": {"type": "x", "message": "가짜-응답-본문"}})

    with pytest.raises(ServiceError) as raised:
        LlmReportGenerator(client=sdk_client(handler)).generate(request_model)
    assert raised.value.status == expected


def test_connection_failure_is_503_and_logs_no_details(request_model, caplog):
    def handler(_: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("가짜-연결-오류-상세")

    with caplog.at_level("DEBUG", logger="moneymate.analysis"), pytest.raises(ServiceError) as raised:
        LlmReportGenerator(client=sdk_client(handler)).generate(request_model)
    assert raised.value.status == 503
    assert "가짜-연결-오류-상세" not in caplog.text
    assert "type=APIConnectionError" in caplog.text


@pytest.mark.parametrize(
    "report, stop_reason, code",
    [(None, "refusal", "llm_refused"), (REPORT, "max_tokens", "llm_invalid_output"), ({"summary": "x"}, "end_turn", "llm_invalid_output")],
)
def test_unusable_model_output_is_500(request_model, report, stop_reason, code):
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=message_body(report, stop_reason))

    with pytest.raises(ServiceError) as raised:
        LlmReportGenerator(client=sdk_client(handler)).generate(request_model)
    assert (raised.value.status, raised.value.code) == (500, code)


def test_usage_is_logged_without_report_text(request_model, caplog):
    client = sdk_client(lambda _: httpx.Response(200, json=message_body()))
    with caplog.at_level("INFO", logger="moneymate.analysis"):
        LlmReportGenerator(client=client).generate(request_model)
    assert f"promptVersion={PROMPT_VERSION}" in caplog.text
    assert "inputTokens=1200" in caplog.text and "outputTokens=300" in caplog.text
    assert "154,500원" not in caplog.text


def test_endpoint_rejects_uncitable_evidence_from_model(request_json):
    bad = dict(REPORT, summary={"text": "가짜 문장", "evidence_ids": ["not.in.facts"]})
    generator = LlmReportGenerator(client=sdk_client(lambda _: httpx.Response(200, json=message_body(bad))))
    client = TestClient(create_app(service_key=KEY, generator=generator), raise_server_exceptions=False)
    response = client.post(PATH, json=request_json, headers=AUTH)
    assert response.status_code == 500
    assert response.json() == {"code": "invalid_report_output"}


def test_generator_selected_from_env(monkeypatch):
    monkeypatch.setenv("ANALYSIS_GENERATOR", "unknown")
    with pytest.raises(RuntimeError):
        create_app(service_key=KEY)
    monkeypatch.setenv("ANALYSIS_GENERATOR", "llm")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "fictional-anthropic-key")
    assert create_app(service_key=KEY) is not None
