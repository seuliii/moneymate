import json
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient

from app.contracts import MonthlyReportRequest
from app.errors import ServiceError
from app.main import create_app
from app.ollama import OllamaReportGenerator
from app.prompt import PROMPT_VERSION
from app.validation import validate_response

EXAMPLES = Path(__file__).resolve().parents[2] / "docs" / "api" / "examples"
KEY = "fictional-test-key"
PATH = "/internal/v1/reports/monthly"
AUTH = {"Authorization": f"Bearer {KEY}"}

REPORT = {
    "summary": {"text": "등록된 9월 지출은 154,500원으로 전월보다 34,500원 증가했습니다.", "evidence_ids": ["expense.total", "comparison.delta"]},
    "highlights": [{"text": "쇼핑 지출은 120,000원으로 전체 지출의 77.67%입니다.", "evidence_ids": ["category.expense_shopping.amount", "category.expense_shopping.share"]}],
    "changes_to_review": [],
    "suggestions": [],
}


@pytest.fixture
def request_json() -> dict:
    return json.loads((EXAMPLES / "monthly-request.json").read_text(encoding="utf-8"))


@pytest.fixture
def request_model(request_json) -> MonthlyReportRequest:
    return MonthlyReportRequest.model_validate(request_json)


def chat_body(content, done_reason="stop") -> dict:
    return {
        "model": "qwen2.5:7b",
        "message": {"role": "assistant", "content": content if isinstance(content, str) else json.dumps(content, ensure_ascii=False)},
        "done": True,
        "done_reason": done_reason,
        "prompt_eval_count": 1500,
        "eval_count": 250,
    }


def generator(handler) -> OllamaReportGenerator:
    return OllamaReportGenerator(client=httpx.Client(transport=httpx.MockTransport(handler)))


def test_generates_contract_valid_report_and_sends_expected_request(request_model):
    captured = {}

    def handler(http_request: httpx.Request) -> httpx.Response:
        captured["url"] = str(http_request.url)
        captured["body"] = json.loads(http_request.content)
        return httpx.Response(200, json=chat_body(REPORT))

    response = generator(handler).generate(request_model)

    assert validate_response(request_model, response) == []
    assert response.request_id == request_model.request_id
    body = captured["body"]
    assert captured["url"] == "http://127.0.0.1:11434/api/chat"
    assert body["model"] == "qwen2.5:7b" and body["stream"] is False
    assert body["options"]["temperature"] == 0
    assert set(body["format"]["required"]) == {"summary", "highlights", "changes_to_review", "suggestions"}
    assert [m["role"] for m in body["messages"]] == ["system", "user"]
    assert "requestId" not in json.loads(body["messages"][1]["content"])


@pytest.mark.parametrize("status, expected", [(500, 503), (503, 503), (404, 500), (400, 500)])
def test_http_errors_map_to_service_status(request_model, status, expected):
    with pytest.raises(ServiceError) as raised:
        generator(lambda _: httpx.Response(status, json={"error": "가짜-오류-본문"})).generate(request_model)
    assert raised.value.status == expected


@pytest.mark.parametrize("error", [httpx.ConnectError("가짜-연결-오류-상세"), httpx.ReadTimeout("가짜-시간-초과")])
def test_transport_failure_is_503_and_logs_no_details(request_model, caplog, error):
    def handler(_: httpx.Request) -> httpx.Response:
        raise error

    with caplog.at_level("DEBUG", logger="moneymate.analysis"), pytest.raises(ServiceError) as raised:
        generator(handler).generate(request_model)
    assert raised.value.status == 503
    assert "가짜" not in caplog.text
    assert f"type={type(error).__name__}" in caplog.text


@pytest.mark.parametrize(
    "content, done_reason",
    [(REPORT, "length"), ('{"summary": ', "stop"), ({"summary": "x"}, "stop")],
)
def test_unusable_model_output_is_invalid_output(request_model, content, done_reason):
    with pytest.raises(ServiceError) as raised:
        generator(lambda _: httpx.Response(200, json=chat_body(content, done_reason))).generate(request_model)
    assert (raised.value.status, raised.value.code) == (500, "llm_invalid_output")


def test_usage_is_logged_without_report_text(request_model, caplog):
    with caplog.at_level("INFO", logger="moneymate.analysis"):
        generator(lambda _: httpx.Response(200, json=chat_body(REPORT))).generate(request_model)
    assert f"promptVersion={PROMPT_VERSION}" in caplog.text
    assert "inputTokens=1500" in caplog.text and "outputTokens=250" in caplog.text
    assert "154,500원" not in caplog.text


def test_endpoint_rejects_uncitable_evidence_from_model(request_json):
    bad = dict(REPORT, summary={"text": "가짜 문장", "evidence_ids": ["not.in.facts"]})
    app = create_app(service_key=KEY, generator=generator(lambda _: httpx.Response(200, json=chat_body(bad))))
    response = TestClient(app, raise_server_exceptions=False).post(PATH, json=request_json, headers=AUTH)
    assert response.status_code == 500
    assert response.json() == {"code": "invalid_report_output"}


def test_ollama_selected_from_env(monkeypatch):
    monkeypatch.setenv("ANALYSIS_GENERATOR", "ollama")
    monkeypatch.setenv("ANALYSIS_LLM_MODEL", "gemma3:12b")
    assert create_app(service_key=KEY) is not None
