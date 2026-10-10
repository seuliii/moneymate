"""실제 LLM으로 표본 리포트를 만들어 계약 검증·지연을 확인한다.

사용 (analysis 폴더에서, Ollama 실행 중):
    .venv/bin/python -m tools.sample_reports --model qwen2.5:7b
표본은 저장소 예제(완료된 9월)와, 이를 바꾼 진행 중인 월·전월 기록 없음 상황이다.
생성 문장을 화면에 출력하므로 가상 예제 데이터로만 실행한다.
"""

import argparse
import copy
import json
import time
from pathlib import Path

from app.contracts import MonthlyReportRequest
from app.errors import ServiceError
from app.ollama import DEFAULT_MODEL, DEFAULT_URL, OllamaReportGenerator
from app.validation import validate_response

EXAMPLE = Path(__file__).resolve().parents[2] / "docs" / "api" / "examples" / "monthly-request.json"


def samples() -> dict:
    base = json.loads(EXAMPLE.read_text(encoding="utf-8"))

    current = copy.deepcopy(base)
    current.update(asOfDate="2026-10-10", month="2026-10", isCalendarMonthComplete=False)
    current["period"] = {"start": "2026-10-01", "endInclusive": "2026-10-10", "days": 10}
    current["comparison"].update(
        mode="same_day", previousPeriod={"start": "2026-09-01", "endInclusive": "2026-09-10", "days": 10}, differentDayCounts=False
    )

    no_previous = copy.deepcopy(base)
    no_previous["comparison"]["status"] = "no_previous_records"
    for fact in no_previous["facts"]:
        if fact["id"] == "comparison.previousExpense":
            fact["value"] = 0  # C#은 전월 기간이 있으면 기록이 없어도 0을 보낸다.
        elif fact["id"].startswith("comparison."):
            fact["value"] = None
        elif fact["id"].endswith((".delta", ".rate")):
            fact["value"] = None
    for category in no_previous["categories"]:
        category.update(previousAmount=None, deltaAmount=None, changeRatePercent=None)

    return {"완료된 월(9월)": base, "진행 중인 월(10/10 기준)": current, "전월 기록 없음": no_previous}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--timeout", type=float, default=60.0, help="표본 확인용이라 운영(20초)보다 길게 둔다.")
    args = parser.parse_args()

    generator = OllamaReportGenerator(base_url=args.url, model=args.model, timeout_seconds=args.timeout)
    for name, payload in samples().items():
        request = MonthlyReportRequest.model_validate(payload)
        started = time.monotonic()
        try:
            response = generator.generate(request)
        except ServiceError as error:
            print(f"\n## {name}: 실패 {error.status} {error.code} ({time.monotonic() - started:.1f}s)")
            continue
        elapsed = time.monotonic() - started
        errors = validate_response(request, response)
        print(f"\n## {name}: {elapsed:.1f}s, 계약 검증 {'통과' if not errors else '실패 ' + str(errors)}")
        report = response.report
        print(f"- summary: {report.summary.text}  {report.summary.evidence_ids}")
        for section in ("highlights", "changes_to_review", "suggestions"):
            for item in getattr(report, section):
                print(f"- {section}: {item.text}  {item.evidence_ids}")


if __name__ == "__main__":
    main()
