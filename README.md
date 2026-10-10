# MoneyMate AI

[개발 현황](docs/development-status.md) · [검증 기록](docs/verification.md) · [파트너 계약/JSON](docs/api/partner-contract-v1.md)

C# 웹과 Python 분석 서비스를 함께 실행하려면 [로컬 통합 테스트](docs/local-integration.md)를 따른다. 한 번의 명령으로 HTTP 연결 모드에서 실행하고 Ctrl+C로 함께 종료할 수 있다. 기본은 규칙 기반 스텁이며 `-Generator gemini`로 실제 AI(Gemini) 리포트를 생성한다.

회원 기능과 사용자별 거래 등록·조회·수정·삭제 화면/API를 제공합니다. 월·유형·카테고리 필터와 페이지 이동을 지원합니다. 로그인하면 홈 실제 합계와 월별 통계 대시보드를 제공합니다. 모의 소비 리포트와 파트너 Python 분석 서비스(HTTP) 연결을 제공합니다. Python 서비스는 스텁 또는 실제 LLM(Gemini 등)으로 리포트를 생성합니다.

회원가입: `/Account/Register` → 로그인: `/Account/Login` → 내 정보: `/Account`. 가입 직후 자동 로그인하지 않으며, 로그인은 브라우저 세션 쿠키로 유지합니다. 이름과 이메일은 현재 로그인 계정에서 조회합니다.

## 실행

Visual Studio에서 `MoneyMate.slnx`를 열고 MoneyMate 프로젝트의 **http** 프로필로 실행합니다. 기본 주소는 **http://localhost:5077**입니다. HTTPS 프로필은 https://localhost:7035를 사용하며 개발 인증서 신뢰 설정이 필요할 수 있습니다.

터미널 사용 시 솔루션 폴더에서:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.packages'
dotnet restore
dotnet run --project MoneyMate --launch-profile http
```

로컬 도구와 패키지 캐시는 위 경로에 두며 Git에서 제외합니다. Visual Studio의 기본 NuGet 캐시를 사용해도 됩니다.

DB 연결 정보가 없어도 기본 화면과 `/health`는 실행됩니다. DB 진단은 버튼을 눌렀을 때 수행하므로 홈 화면을 막지 않습니다.

## PostgreSQL 최초 설정

PostgreSQL 서버 설치와 프로젝트 DB 생성은 별도입니다. 현재 확인된 시스템 PostgreSQL은 localhost:5432입니다.

프로젝트 DB가 아직 없다면 솔루션 폴더의 PowerShell에서 다음을 실행합니다.

```powershell
pwsh -NoProfile -File .\scripts\Initialize-Database.ps1
```

`pwsh`가 없다면 Windows PowerShell에서 `powershell -NoProfile -File .\scripts\Initialize-Database.ps1`로 실행할 수 있습니다. 실행 정책이 차단하면 스크립트를 확인하고 조직/PC 정책에 맞게 허용하세요.

1. 설치할 때 설정한 **postgres 관리자 비밀번호**를 PC 터미널에 입력합니다. 입력은 숨겨집니다. 채팅에 보내지 마세요.
2. 스크립트가 일반 앱 계정 `moneymate_app`과 DB `moneymate`를 생성합니다. 슈퍼유저 권한은 부여하지 않습니다.
3. 앱 비밀번호를 임의 생성해 `MoneyMate/appsettings.Local.json`에 저장합니다. 관리자 비밀번호는 저장하지 않습니다.
4. 초기 EF 마이그레이션을 적용해 Identity 및 가계부 테이블과 기본 카테고리 17개를 생성합니다.
5. 웹을 실행/재실행하고 **DB 연결 확인** 버튼에서 정상 상태를 확인합니다. pgAdmin의 Databases 목록도 새로고침합니다.

이미 같은 이름의 DB/계정 또는 로컬 설정이 있으면 덮어쓰지 않고 중단합니다. DB 생성은 PostgreSQL 특성상 계정 생성과 하나의 트랜잭션으로 묶지 않으며, 일부 단계 실패 시 생성된 항목을 유지하고 안내합니다.

`appsettings.Local.json`에는 앱 비밀번호가 들어 있습니다. `.gitignore`로 제외되며 프로젝트 설정에서 빌드·게시 복사도 모두 금지합니다. 개발 PC에서만 사용하고 공유·스크린샷·커밋하지 마세요. 공개 배포에서는 환경변수 또는 배포 비밀 저장소를 사용합니다. 기존 배포 폴더가 있다면 예전 복사본이 남지 않았는지 확인하세요.

## 기존 DB를 사용할 때

직접 만든 앱 계정을 사용하려면 `MoneyMate/appsettings.Local.json`을 다음 형식으로 생성합니다. 실제 비밀번호를 넣되 커밋하지 마세요. 이 예제의 비밀번호는 접속용으로 사용할 수 없습니다.

```json
{
  "ConnectionStrings": {
    "MoneyMate": "Host=localhost;Port=5432;Database=moneymate;Username=moneymate_app;Password=YOUR_LOCAL_PASSWORD;Timeout=5;Command Timeout=5"
  }
}
```

또는 개발 User Secrets/`ConnectionStrings__MoneyMate` 환경변수를 사용할 수 있습니다. 환경변수 > User Secrets > 개발용 로컬 JSON 순서로 우선 적용됩니다. 운영 환경에서는 로컬 JSON과 User Secrets를 읽지 않습니다. 비밀번호에 연결 문자열 특수문자가 있으면 Npgsql 연결 문자열 인용 규칙에 맞게 구성하세요.

마이그레이션 적용은 **MoneyMate 프로젝트 폴더**에서 수행해야 로컬 JSON을 읽습니다.

```powershell
# 솔루션 폴더에서 시작
$env:DOTNET_CLI_HOME = Join-Path $PWD '.cli-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.packages'
dotnet tool restore
Set-Location .\MoneyMate
dotnet ef database update
```

웹 시작 시 DB를 생성하거나 마이그레이션을 자동 실행하지 않습니다. 스키마 변경은 개발자가 명시적으로 적용합니다.

## 구조

| 폴더 | 책임 |
|---|---|
| Pages | 기본 화면·레이아웃·오류 안내 |
| wwwroot | CSS·개발용 연결 확인 JavaScript |
| Models | 사용자·거래·카테고리·데이터 버전·리포트 모델 |
| Data | DbContext·카테고리 시드·마이그레이션 |
| Services | DB 연결·스키마 진단 |
| Contracts | 진단 응답 DTO |
| scripts | 실제 개발 DB 초기화 |
| docs | 설계 및 검증 기록 |

인증용 테이블을 사용하는 회원 기능과 인증 쿠키, 거래·통계·리포트 API를 제공합니다. 스키마 변경을 반영할 때는 명시적으로 마이그레이션을 적용합니다.

## 회원 기능과 보안

- ASP.NET Core Identity로 비밀번호를 해싱해 저장합니다.
- 가입 정보: 이메일(254자 이하), 이름(공백 제외 1~50자), 비밀번호·확인.
- 비밀번호: 8~128자, 영문 대문자·소문자·숫자 포함. 특수문자는 선택입니다.
- 이메일은 앞뒤 공백을 제거해 저장하고 Identity 정규화 값으로 중복을 검사합니다. 이메일과 사용자 이름을 동일하게 저장하며 사용자 이름의 DB 고유 인덱스로 동시 가입 중복도 막습니다.
- 사용자 생성과 거래 데이터 버전 상태 생성은 같은 DB 트랜잭션에서 처리합니다.
- 비밀번호 실패 5회 시 해당 계정은 5분간 잠깁니다. 없는 계정과 잠긴 계정의 로그인 안내는 동일합니다.
- 회원가입·로그인 API와 해당 화면은 IP당 1분 30회 요청 제한을 공유합니다. 화면 GET도 포함되며 초과 시 429입니다. 이 제한은 단일 앱 인스턴스의 메모리 기준입니다.
- 인증 쿠키: HttpOnly, SameSite=Lax, 세션 쿠키, 인증 유효 시간 8시간 및 활동 시 갱신. 운영에서는 Secure를 강제합니다.
- 모든 상태 변경 API와 Razor 폼은 CSRF 검증을 적용합니다. 로그아웃은 POST로만 수행합니다.
- 로그인 후 이동은 로컬 URL만 허용합니다. 이름 등 사용자 입력은 Razor에서 HTML 인코딩됩니다.
- API 미인증은 401, 인증이 필요한 화면은 로그인 화면으로 이동합니다.
- 이메일 확인, 비밀번호 재설정, 계정 탈퇴는 후속 범위입니다. 이메일 확인 기능이 없으므로 실제 이메일 소유를 검증하지 않습니다.

## 인증 API

| 메서드·경로 | 동작 |
|---|---|
| GET /api/auth/csrf | CSRF 쿠키와 requestToken 발급 |
| POST /api/auth/register | 가입, 성공 201 / 중복 409 / 입력 오류 400 |
| POST /api/auth/login | 로그인 쿠키 발급, 성공 200 / 실패 401 |
| POST /api/auth/logout | 인증 쿠키 해제, 성공 204 |
| GET /api/auth/me | 로그인한 사용자 정보, 미인증 401 |

API 클라이언트는 쿠키를 보존하고 `/api/auth/csrf` 응답의 requestToken을 `X-CSRF-TOKEN` 헤더로 POST에 전달합니다. 로그인·로그아웃으로 인증 상태가 바뀌면 새 CSRF 토큰을 발급받아 사용합니다. GET 응답은 캐시하지 않습니다.

회원가입 JSON 필드: `email`, `displayName`, `password`, `confirmPassword`. 로그인 JSON 필드: `email`, `password`. 사용자 ID는 입력받지 않습니다. 내 정보 응답에는 `id`, `email`, `displayName`, `createdAt`만 포함하고 비밀번호·해시·보안 스탬프는 반환하지 않습니다.

## 거래 화면과 API

로그인 후 `/Transactions`에서 월·유형·카테고리로 조회합니다. 등록은 `/Transactions/Create`, 수정과 삭제 확인 화면은 목록의 링크로 이동합니다. 삭제는 확인 체크와 POST 제출이 필요합니다.

| 메서드·경로 | 동작 |
|---|---|
| GET /api/categories?type=expense | 공통 카테고리, type 생략 시 전체 |
| GET /api/transactions?month=2026-10&type=expense&page=1&pageSize=20 | 내 거래 조회, categoryId 필터 선택 가능 |
| GET /api/transactions/{id} | 내 거래 상세 |
| POST /api/transactions | 등록, 성공 201 |
| PUT /api/transactions/{id} | 수정, 성공 200 / 버전 충돌 409 |
| DELETE /api/transactions/{id}?version=1 | 삭제, 성공 204 / 버전 충돌 409 |

등록 JSON: `type`(income/expense), `amount`(1~1,000,000,000 원의 정수), `categoryId`, `transactionDate`(yyyy-MM-dd), `title`(공백 제외 1~100자), `memo`(선택, 500자 이하). 수정은 조회한 `version`을 추가합니다. 미래 거래일·조회 월과 거래 유형에 맞지 않는 카테고리는 거절합니다.

목록 응답에는 `items`, `month`, `page`, `pageSize`, `totalCount`, `totalPages`가 포함됩니다. 거래일 내림차순 후 ID 내림차순으로 정렬하며 기본 크기는 20, 최대 100입니다. 마지막 페이지를 넘는 요청은 마지막 유효 페이지로 보정하며 빈 목록은 1 / 1 페이지입니다. 다른 사용자의 거래는 상세·수정·삭제 모두 404로 응답합니다. 변경 요청은 인증 쿠키와 CSRF 토큰이 필요하며, 성공 응답의 `X-Ledger-Data-Version` 헤더로 갱신된 사용자 데이터 버전을 확인합니다.

## 월별 통계

로그인 후 `/Dashboard`에서 조회 월을 선택합니다. `/api/statistics/monthly?month=2026-09`는 본인의 동일 통계를 JSON으로 반환하며 month 생략 시 현재 한국 월입니다. 미래 월과 잘못된 형식은 400입니다. 홈도 현재 월의 실제 합계를 표시합니다.

총 수입·지출·월간 수지, 달력 일수 기준 일평균 지출, 카테고리 금액·비중, 최다 카테고리·최대 거래, 전월 지출 및 카테고리 비교를 제공합니다. 현재 월은 오늘까지와 전월 같은 일자까지 비교하며 전월 말일로 제한할 수 있습니다. 과거 월은 전체 월끼리 비교합니다. 기록 없음과 전월 지출 0을 구분하고 계산 불가 비율은 null입니다. 월간 수지는 계좌 잔액과 다릅니다.

통계와 데이터 버전은 하나의 DB 읽기 스냅샷에서 계산합니다. 상세 API 필드·계산 기준·검증 결과는 `docs/stage-4.md`를 참고하세요. 계산 경계 검증은 `dotnet run --project tests/StatisticsChecks`로 실행하며 추가 외부 패키지는 없습니다.

DB 접속/조회 장애 시 통계 API는 503 및 `statistics_unavailable`, 대시보드는 재시도 안내를 반환합니다. 홈은 금액 대신 장애 안내를 표시합니다. 기록이 없는 정상 조회의 0원과 구분합니다.

## 소비 리포트와 파트너 연동

`/Reports`에서 월을 선택하고 모의 리포트를 생성·저장·조회합니다. 실제 AI 결과와 혼동하지 않도록 모의 표시를 제공합니다. 동일 버전·날짜·설정은 캐시를 반환하며 거래/날짜/분석 설정이 바뀌면 재분석 필요를 표시합니다. 실패한 요청은 기존 성공 결과를 지우지 않습니다.

| 메서드·경로 | 동작 |
|---|---|
| POST /api/analysis/reports | JSON month, 신규 201 / 캐시 200, CSRF 필수 |
| GET /api/analysis/reports/latest?month=2026-09 | 내 최근 성공 리포트, 없으면 404 |
| GET /api/analysis/reports/{id} | 내 저장 리포트, 타인 소유 포함 없으면 404 |

빈 기록은 400/no_records, 동일 월 생성 중은 409, 하루 시도 한도 초과는 429, 분석 응답 실패는 502, 시간 초과는 504, DB 장애는 503입니다. API 오류에는 code와 기본 traceId가 포함됩니다.

기본 Analysis 모드는 Mock이며 실제 외부 AI 전송은 없습니다. 실제 Http 모드 설정, 파트너 요청/응답 규격과 가상 JSON은 `docs/api/partner-contract-v1.md`를 참고하세요. 하루 기본 5회와 동시 분석 가드는 단일 프로세스 메모리 기준이며 운영 공유 저장소는 후속입니다. API 키는 환경변수/비밀 저장소만 사용합니다.

계약/HTTP 모의 검증은 `dotnet run --project tests/AnalysisChecks`입니다. 파트너 Python 서비스의 실행과 검증은 `analysis/README.md`를 참고하세요. 기본 DB 마이그레이션에 리포트 테이블이 있어 DB 초기화 재실행은 필요 없습니다.

## 통합 검증

복원·빌드 후 솔루션 폴더에서 `./scripts/Test-DatabaseFoundation.ps1`을 실행합니다. PostgreSQL 테스트 포트 55432와 웹 포트 5078이 필요합니다. 테스트는 별도 DB에만 계정을 생성하며 검증 후 테스트 서버를 종료합니다. Test-Accounts.ps1과 Test-Transactions.ps1은 이 테스트 웹에 연결되는 하위 검증 스크립트입니다.

검증 범위와 해결한 문제는 [검증 기록](docs/verification.md)에 정리했습니다.

## 진단 주소

- `/`: 기본 웹 화면.
- `/health`: 웹 프로세스 생존 여부. DB 연결 성공을 의미하지 않습니다.
- `/api/development/database-status`: 개발 환경에서만 제공. `not_configured`, `unavailable`, `migration_required`, `ready` 중 하나를 반환합니다.
- `/openapi/v1.json`: 개발용 OpenAPI 문서. Swagger UI는 아직 설치하지 않았습니다.

DB 진단은 실제 DB 연결, 미적용 마이그레이션, 카테고리 테이블 조회를 확인합니다. 연결 문자열과 내부 오류 상세는 응답에 포함하지 않습니다. 운영에서는 개발용 화면과 진단 경로를 제공하지 않습니다.

## 모델 기준

- 금액은 원화 양의 정수 `long/bigint`, 거래 유형은 Income=1, Expense=2입니다.
- 사용자 FK, 금액 범위, 카테고리 유형 일치, 거래 제목 길이, 버전, 리포트 월 첫날에 DB 제약을 둡니다.
- 거래일은 DateOnly/date, 생성·수정 시각은 DateTimeOffset/timestamptz입니다. 서비스에서 UTC 저장과 한국 날짜 입력 검증을 적용합니다.
- 거래 버전과 사용자 데이터 버전은 동시성 토큰으로 준비했습니다. 거래 변경과 두 버전 증가는 같은 DB 트랜잭션에서 처리합니다. 사용자 버전 행을 잠가 동시 변경도 순서대로 반영합니다.
- AI 요청·결과는 jsonb로 보존하고 캐시 키에 고유 인덱스를 둡니다.

## 다음 작업

C# ↔ 실제 AI(Gemini) 공동 통합 검증과 배포 준비가 남아 있습니다. 최신 상태는 [개발 현황](docs/development-status.md)을 확인하세요. 코드는 dev에 반영되어 있으며 로컬 개발 폴더는 MoneyMateDev 하나로 통일했습니다.
