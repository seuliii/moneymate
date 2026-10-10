import json
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient

from app.contracts import MonthlyReportRequest
from app.errors import ServiceError
from app.gemini import GeminiReportGenerator
from app.main import create_app
from app.prompt import PROMPT_VERSION
from app.validation import validate_response

EXAMPLES = Path(__file__).resolve().parents[2] / "docs" / "api" / "examples"
KEY = "fictional-test-key"
GEMINI_KEY = "fictional-gemini-key"
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


def gemini_body(content=REPORT, finish_reason="STOP", parts=None, block_reason=None) -> dict:
    text = content if isinstance(content, str) else json.dumps(content, ensure_ascii=False)
    body = {
        "candidates": [{"content": {"role": "model", "parts": parts or [{"text": text}]}, "finishReason": finish_reason}],
        "usageMetadata": {"promptTokenCount": 1500, "candidatesTokenCount": 250, "thoughtsTokenCount": 40},
    }
    if block_reason:
        body = {"promptFeedback": {"blockReason": block_reason}}
    return body


def generator(handler) -> GeminiReportGenerator:
    return GeminiReportGenerator(api_key=GEMINI_KEY, client=httpx.Client(transport=httpx.MockTransport(handler)))


def test_generates_contract_valid_report_and_sends_expected_request(request_model):
    captured = {}

    def handler(http_request: httpx.Request) -> httpx.Response:
        captured["url"] = str(http_request.url)
        captured["headers"] = http_request.headers
        captured["body"] = json.loads(http_request.content)
        return httpx.Response(200, json=gemini_body())

    response = generator(handler).generate(request_model)

    assert validate_response(request_model, response) == []
    assert response.request_id == request_model.request_id
    assert captured["url"] == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent"
    assert GEMINI_KEY not in captured["url"], "키는 URL이 아닌 헤더로 보낸다"
    assert captured["headers"]["x-goog-api-key"] == GEMINI_KEY
    config = captured["body"]["generationConfig"]
    assert config["temperature"] == 0 and config["responseMimeType"] == "application/json"
    assert set(config["responseJsonSchema"]["required"]) == {"summary", "highlights", "changes_to_review", "suggestions"}
    assert config["thinkingConfig"] == {"thinkingLevel": "low"}
    assert "requestId" not in json.loads(captured["body"]["contents"][0]["parts"][0]["text"])


def test_thought_parts_are_excluded(request_model):
    parts = [{"text": "생각 요약", "thought": True}, {"text": json.dumps(REPORT, ensure_ascii=False)}]
    response = generator(lambda _: httpx.Response(200, json=gemini_body(parts=parts))).generate(request_model)
    assert validate_response(request_model, response) == []


@pytest.mark.parametrize("status, expected", [(429, 503), (500, 503), (503, 503), (400, 500), (403, 500), (404, 500)])
def test_http_errors_map_to_service_status(request_model, status, expected):
    with pytest.raises(ServiceError) as raised:
        generator(lambda _: httpx.Response(status, json={"error": {"message": "가짜-오류-본문"}})).generate(request_model)
    assert raised.value.status == expected


def test_transport_failure_is_503_and_logs_no_details(request_model, caplog):
    def handler(_: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("가짜-시간-초과-상세")

    with caplog.at_level("DEBUG", logger="moneymate.analysis"), pytest.raises(ServiceError) as raised:
        generator(handler).generate(request_model)
    assert raised.value.status == 503
    assert "가짜" not in caplog.text and GEMINI_KEY not in caplog.text
    assert "type=ReadTimeout" in caplog.text


@pytest.mark.parametrize(
    "body, code",
    [
        (gemini_body(finish_reason="SAFETY"), "llm_refused"),
        (gemini_body(block_reason="SAFETY"), "llm_refused"),
        (gemini_body(finish_reason="MAX_TOKENS"), "llm_invalid_output"),
        (gemini_body(content='{"summary": '), "llm_invalid_output"),
    ],
)
def test_unusable_model_output_is_500(request_model, body, code):
    with pytest.raises(ServiceError) as raised:
        generator(lambda _: httpx.Response(200, json=body)).generate(request_model)
    assert (raised.value.status, raised.value.code) == (500, code)


def test_usage_is_logged_without_report_text_or_key(request_model, caplog):
    with caplog.at_level("INFO", logger="moneymate.analysis"):
        generator(lambda _: httpx.Response(200, json=gemini_body())).generate(request_model)
    assert f"promptVersion={PROMPT_VERSION}" in caplog.text
    assert "inputTokens=1500" in caplog.text and "outputTokens=250" in caplog.text
    assert "154,500원" not in caplog.text and GEMINI_KEY not in caplog.text


def test_endpoint_rejects_uncitable_evidence_from_model(request_json):
    bad = dict(REPORT, summary={"text": "가짜 문장", "evidence_ids": ["not.in.facts"]})
    app = create_app(service_key=KEY, generator=generator(lambda _: httpx.Response(200, json=gemini_body(bad))))
    response = TestClient(app, raise_server_exceptions=False).post(PATH, json=request_json, headers=AUTH)
    assert response.status_code == 500
    assert response.json() == {"code": "invalid_report_output"}


def test_gemini_selected_from_env_and_requires_key(monkeypatch):
    monkeypatch.setenv("ANALYSIS_GENERATOR", "gemini")
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    with pytest.raises(RuntimeError):
        create_app(service_key=KEY)
    monkeypatch.setenv("GEMINI_API_KEY", GEMINI_KEY)
    assert create_app(service_key=KEY) is not None
