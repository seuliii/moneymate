# 5단계 — 모의 리포트·파트너 분석 계약

상태: 웹 구현/검증 완료. [PR #4](https://github.com/seuliii/moneymate/pull/4)로 dev에 반영했다. 실제 Python/LLM 서비스 연동 완료를 뜻하지 않는다.

- /Reports에서 월별 모의 생성·저장·조회와 요약/특징/변화/제안 및 근거를 제공한다.
- 동일 사용자·월·거래 버전·기준일·계약/모델/프롬프트 설정의 성공 결과는 캐시를 사용한다. 기준이 바뀌면 갱신 필요를 표시하며 실패 시 기존 결과를 유지한다.
- 통계 스냅샷에서 개인정보를 제외한 파트너 입력을 만들고 응답의 필수 필드·버전·requestId·문장/근거/출력 크기를 검증한다.
- 기본 Mock 모드와 선택 가능한 Bearer 인증 HTTP 어댑터를 제공한다. 내부 요청 최대25초/전체 흐름 최대60초,503 또는 네트워크 실패만 같은 requestId로 최대1회 재시도한다.
- 동일 사용자·월 진행 중409, 하루 기본5회 초과429, 빈 기록400, 분석 실패502, 시간 초과504이다. 한도/가드는 현재 단일 프로세스 메모리다.
- 기존 AnalysisReports 테이블을 사용하며 추가 마이그레이션은 없다.

API: POST /api/analysis/reports(신규201/캐시200), GET /latest?month=YYYY-MM, GET /{id}. 조회는 소유자 범위, 변경은 인증·CSRF를 요구한다.

[파트너 계약 및 전체 JSON](api/partner-contract-v1.md) · [검증 기록](verification.md) · [현재 상태/남은 작업](development-status.md)
