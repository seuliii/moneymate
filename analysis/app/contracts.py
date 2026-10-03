"""C# ↔ Python 분석 계약 v1 모델.

기준: docs/api/partner-contract-v1.md, MoneyMate/Contracts/AnalysisContracts.cs.
C#은 camelCase JSON을 주고받고 응답의 알 수 없는 필드를 거절하므로 양방향 모두 extra 필드를 금지한다.
"""

from datetime import date
from decimal import Decimal
from typing import List, Literal, Optional, Set
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel

CONTRACT_VERSION = "1.0"


class ContractModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="forbid", frozen=True)


# --- 요청 ---


class Period(ContractModel):
    start: date
    end_inclusive: date
    days: int = Field(ge=1)


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
