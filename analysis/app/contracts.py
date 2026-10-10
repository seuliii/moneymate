"""C# ↔ Python 분석 계약 v1 모델.

기준: docs/api/partner-contract-v1.md, MoneyMate/Contracts/AnalysisContracts.cs.
C#은 camelCase JSON을 주고받고 응답의 알 수 없는 필드를 거절하므로 양방향 모두 extra 필드를 금지한다.
"""

import calendar
from datetime import date, timedelta
from decimal import Decimal
from typing import List, Literal, Optional, Set
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel

CONTRACT_VERSION = "1.0"


class ContractModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid", frozen=True)


# --- 요청 ---


def _month_bounds(year: int, month: int) -> "tuple[date, date]":
    return date(year, month, 1), date(year, month, calendar.monthrange(year, month)[1])


class Period(ContractModel):
    start: date
    end_inclusive: date
    days: int = Field(ge=1)

    @model_validator(mode="after")
    def _check_days(self) -> "Period":
        # 같은 달 안의 기간이며 days는 종료일 포함 일수와 같아야 한다.
        if (self.start.year, self.start.month) != (self.end_inclusive.year, self.end_inclusive.month):
            raise ValueError("기간은 한 달 안에 있어야 합니다.")
        if self.days != (self.end_inclusive - self.start).days + 1:
            raise ValueError("days가 기간 일수와 다릅니다.")
        return self


class Fact(ContractModel):
    id: str = Field(min_length=1)
    # null은 비교 불가. 0으로 바꾸거나 근거로 인용하지 않는다.
    value: Optional[Decimal]
    unit: Literal["KRW", "percent"]


class Category(ContractModel):
    code: str = Field(min_length=1)
    name: str = Field(min_length=1)
    amount: int = Field(ge=0)
    previous_amount: Optional[int]
    delta_amount: Optional[int]
    change_rate_percent: Optional[Decimal]
    share_percent: Optional[Decimal]


class Comparison(ContractModel):
    status: Literal["comparable", "no_previous_records", "zero_previous_expense"]
    mode: Literal["full_month", "same_day"]
    previous_period: Optional[Period]
    different_day_counts: bool


class LargestExpense(ContractModel):
    date: date
    category_code: str = Field(min_length=1)
    amount: int = Field(ge=1)


class MonthlyReportRequest(ContractModel):
    contract_version: Literal["1.0"]
    request_id: UUID
    locale: Literal["ko-KR"]
    currency: Literal["KRW"]
    time_zone: Literal["Asia/Seoul"]
    data_version: int = Field(ge=0)
    as_of_date: date
    month: str = Field(pattern=r"^[0-9]{4}-[0-9]{2}$")
    is_calendar_month_complete: bool
    period: Period
    recording_coverage: str
    record_count: int = Field(ge=0)
    facts: List[Fact]
    categories: List[Category]
    comparison: Comparison
    largest_expenses: List[LargestExpense]
    limitations: List[str]

    @model_validator(mode="after")
    def _check_month_and_periods(self) -> "MonthlyReportRequest":
        # C# StatisticsCalculator.Period/PreviousPeriod와 같은 기준으로 월·기간 일치를 확인한다.
        year, month = int(self.month[:4]), int(self.month[5:])
        if not 1 <= month <= 12:
            raise ValueError("month가 올바르지 않습니다.")
        first, last = _month_bounds(year, month)
        if self.period.start != first:
            raise ValueError("period.start가 대상 월 1일이 아닙니다.")
        if self.is_calendar_month_complete:
            if self.period.end_inclusive != last or self.as_of_date <= last:
                raise ValueError("완료된 월의 기간이 말일까지가 아닙니다.")
        elif self.period.end_inclusive != self.as_of_date:
            raise ValueError("진행 중인 월의 기간이 기준일까지가 아닙니다.")
        expected_mode = "full_month" if self.is_calendar_month_complete else "same_day"
        if self.comparison.mode != expected_mode:
            raise ValueError("comparison.mode가 월 완료 여부와 다릅니다.")

        previous = self.comparison.previous_period
        if previous is None:
            if self.comparison.status != "no_previous_records":
                raise ValueError("비교 가능한 상태에는 previousPeriod가 필요합니다.")
            return self
        previous_month_end = first - timedelta(days=1)
        prev_first, prev_last = _month_bounds(previous_month_end.year, previous_month_end.month)
        expected_days = (prev_last - prev_first).days + 1
        if self.comparison.mode == "same_day":
            expected_days = min(self.period.end_inclusive.day, expected_days)
        if previous.start != prev_first or previous.days != expected_days:
            raise ValueError("previousPeriod가 비교 기준과 다릅니다.")
        if self.comparison.different_day_counts != (previous.days != self.period.days):
            raise ValueError("differentDayCounts가 기간 일수와 다릅니다.")
        return self

    def fact_value(self, fact_id: str) -> Optional[Decimal]:
        for fact in self.facts:
            if fact.id == fact_id:
                return fact.value
        return None

    def citable_fact_ids(self) -> Set[str]:
        """근거로 인용 가능한 ID: 요청 facts 중 값이 null이 아닌 것."""
        return {fact.id for fact in self.facts if fact.value is not None}


# --- 응답 ---


class ReportStatement(ContractModel):
    text: str
    evidence_ids: List[str]


class ReportContent(ContractModel):
    summary: ReportStatement
    highlights: List[ReportStatement]
    changes_to_review: List[ReportStatement]
    suggestions: List[ReportStatement]


class MonthlyReportResponse(ContractModel):
    contract_version: str
    request_id: UUID
    report: ReportContent
