"""응답 계약 검증. C# AnalysisContract.Valid와 같은 기준으로 먼저 걸러 C#의 502를 예방한다."""

from typing import List, Optional

from .contracts import CONTRACT_VERSION, MonthlyReportRequest, MonthlyReportResponse, ReportStatement

MAX_SECTION_ITEMS = 3
MAX_TEXT_LENGTH = 500
MAX_EVIDENCE_IDS = 20
MAX_RESPONSE_BYTES = 65_536


def _utf16_length(text: str) -> int:
    # C# string.Length는 UTF-16 코드 단위를 센다. 이모지 등은 Python len()보다 길게 계산된다.
    return len(text.encode("utf-16-le")) // 2


def _check_statement(path: str, item: ReportStatement, citable: set, errors: List[str]) -> None:
    if not item.text.strip():
        errors.append(f"{path}.text: 비어 있습니다.")
    if _utf16_length(item.text) > MAX_TEXT_LENGTH:
        errors.append(f"{path}.text: {MAX_TEXT_LENGTH}자를 초과합니다.")
    if not 1 <= len(item.evidence_ids) <= MAX_EVIDENCE_IDS:
        errors.append(f"{path}.evidenceIds: 1~{MAX_EVIDENCE_IDS}개여야 합니다.")
    for evidence_id in item.evidence_ids:
        if evidence_id not in citable:
            # 출력 값은 로그로 이어지므로 생성기가 만든 ID 문자열을 메시지에 넣지 않는다.
            errors.append(f"{path}.evidenceIds: 인용할 수 없는 근거가 있습니다.")
            break


def validate_response(
    request: MonthlyReportRequest, response: MonthlyReportResponse, serialized: Optional[bytes] = None
) -> List[str]:
    """계약 위반 목록을 반환한다. 빈 목록이면 통과."""
    errors: List[str] = []
    if response.contract_version != CONTRACT_VERSION:
        errors.append("contractVersion: 요청과 다릅니다.")
    if response.request_id != request.request_id:
        errors.append("requestId: 요청과 다릅니다.")
    if serialized is not None and len(serialized) > MAX_RESPONSE_BYTES:
        errors.append(f"응답이 {MAX_RESPONSE_BYTES}바이트를 초과합니다.")

    citable = request.citable_fact_ids()
    report = response.report
    _check_statement("summary", report.summary, citable, errors)
    for name, items in (
        ("highlights", report.highlights),
        ("changesToReview", report.changes_to_review),
        ("suggestions", report.suggestions),
    ):
        if len(items) > MAX_SECTION_ITEMS:
            errors.append(f"{name}: 최대 {MAX_SECTION_ITEMS}개입니다.")
        for index, item in enumerate(items):
            _check_statement(f"{name}[{index}]", item, citable, errors)
    return errors
