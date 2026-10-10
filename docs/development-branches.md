# 개발 기능별 브랜치

현재 로컬 코드에서 기능을 분리한 누적 스냅샷이다. 과거 단계별 Git 이력을 복원한 것이 아니며, 각 브랜치는 이전 기능을 포함한다. 기획/환경 설치 전용 브랜치는 만들지 않았다. 실행에 필요한 프로젝트, DB 스키마, 마이그레이션, 초기화 도구는 첫 회원 기능 브랜치의 공통 기반이다.

| 개발 단계 | 브랜치 | 기능 |
|---|---|---|
| 2 | feature/member-authentication | 회원가입, 로그인, 로그아웃, 내 계정, 인증/CSRF |
| 3 | feature/transaction-management | 사용자별 거래 CRUD, 필터, 페이지 이동, 동시 수정 방지 |
| 4 | feature/monthly-statistics | 월별 집계, 카테고리, 전월 비교, 홈과 대시보드 |
| 5 | feature/mock-reports-and-analysis-contract | 모의 리포트, 저장/캐시, HTTP 어댑터, 파트너 계약/JSON |

각 기능 브랜치를 PR #1부터 #4까지 순서대로 dev에 병합했다. main은 변경하지 않는다. 실제 Python/LLM 구현과 서비스 연동 검증은 파트너 서비스 준비 후 진행한다.

파트너 계약의 기준 파일은 docs/api/partner-contract-v1.md, 예제는 docs/api/examples/monthly-request.json 및 monthly-response.json이다. Wiki는 이 파일로 연결하는 안내 페이지로 사용해 코드와 계약의 버전이 함께 관리되도록 한다.

현재 로컬 개발 폴더는 MoneyMateDev 하나이며 기존 Git 이력을 연결했다. 이후 작업은 새 브랜치와 dev 대상 PR로 관리한다. 진행 상태는 [개발 현황](development-status.md)을 확인한다.
