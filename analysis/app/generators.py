"""리포트 생성기.

ReportGenerator를 구현하면 엔드포인트 변경 없이 생성 방식을 바꿀 수 있다.
StubReportGenerator는 LLM 없이 요청 facts만으로 문장을 만드는 규칙 기반 구현이며,
C#과의 HTTP·계약 연결 확인용이다. 실제 LLM 생성기는 이후 단계에서 추가한다.
"""

from decimal import ROUND_HALF_UP, Decimal
from typing import List, Protocol

from .contracts import (
    CONTRACT_VERSION,
    MonthlyReportRequest,
    MonthlyReportResponse,
    ReportContent,
    ReportStatement,
)


class ReportGenerator(Protocol):
    def generate(self, request: MonthlyReportRequest) -> MonthlyReportResponse: ...


def _won(value: Decimal) -> str:
    return f"{abs(value).quantize(Decimal(1), rounding=ROUND_HALF_UP):,}원"


def _percent(value: Decimal) -> str:
    return f"{value.normalize():f}%"


def _month_label(month: str) -> str:
    return f"{int(month[5:])}월"


def _comparison_base(request: MonthlyReportRequest) -> str:
    # 진행 중인 월은 전월 같은 일자까지와 비교하므로 요약·카테고리 문장 모두 같은 표현을 쓴다.
    return "전월 같은 기간" if request.comparison.mode == "same_day" else "전월"


class StubReportGenerator:
    def generate(self, request: MonthlyReportRequest) -> MonthlyReportResponse:
        report = ReportContent(
            summary=self._summary(request),
            highlights=self._highlights(request),
            changes_to_review=self._changes_to_review(request),
            suggestions=self._suggestions(request),
        )
        return MonthlyReportResponse(contract_version=CONTRACT_VERSION, request_id=request.request_id, report=report)

    def _summary(self, request: MonthlyReportRequest) -> ReportStatement:
        month = _month_label(request.month)
        expense = request.fact_value("expense.total") or Decimal(0)
        delta = request.fact_value("comparison.delta")
        text = f"등록된 {month} 지출은 {_won(expense)}입니다."
        evidence = ["expense.total"]
        if delta is not None and request.comparison.status == "comparable":
            base = _comparison_base(request)
            if delta == 0:
                text = f"등록된 {month} 지출은 {_won(expense)}으로 {base}과 같습니다."
            else:
                direction = "증가" if delta > 0 else "감소"
                text = f"등록된 {month} 지출은 {_won(expense)}으로 {base}보다 {_won(delta)} {direction}했습니다."
            evidence.append("comparison.delta")
        return ReportStatement(text=text, evidence_ids=evidence)

    def _highlights(self, request: MonthlyReportRequest) -> List[ReportStatement]:
        items = []
        for category in sorted(request.categories, key=lambda c: c.amount, reverse=True)[:2]:
            if category.amount <= 0:
                continue
            prefix = f"category.{category.code}"
            if category.share_percent is not None:
                text = f"{category.name} 지출은 {_won(Decimal(category.amount))}으로 전체 지출의 {_percent(category.share_percent)}입니다."
                evidence = [f"{prefix}.amount", f"{prefix}.share"]
            else:
                text = f"{category.name} 지출은 {_won(Decimal(category.amount))}입니다."
                evidence = [f"{prefix}.amount"]
            items.append(ReportStatement(text=text, evidence_ids=evidence))
        return items

    def _changes_to_review(self, request: MonthlyReportRequest) -> List[ReportStatement]:
        if request.comparison.status != "comparable":
            return []
        increased = [c for c in request.categories if c.delta_amount is not None and c.delta_amount > 0]
        base = _comparison_base(request)
        items = []
        for category in sorted(increased, key=lambda c: c.delta_amount, reverse=True)[:2]:
            text = f"{category.name} 지출이 {base}보다 {_won(Decimal(category.delta_amount))} 증가했습니다. 일회성 지출인지 확인해볼 수 있습니다."
            items.append(ReportStatement(text=text, evidence_ids=[f"category.{category.code}.delta"]))
        return items

    def _suggestions(self, request: MonthlyReportRequest) -> List[ReportStatement]:
        spent = [c for c in request.categories if c.amount > 0]
        if not spent:
            return [ReportStatement(text="누락된 거래가 있는지 확인하고 기록을 이어가보세요.", evidence_ids=["expense.total"])]
        top = max(spent, key=lambda c: c.amount)
        return [ReportStatement(text=f"다음 달 {top.name} 예정 항목을 미리 기록해보세요.", evidence_ids=[f"category.{top.code}.amount"])]
