# 파트너 분석 API 계약 초안 v1

상태: **파트너 검토 전 초안**. 현재 웹은 로컬 모의 응답을 사용합니다. Python/LLM 구현 및 실제 서비스 연동 검증은 파트너 서비스 준비 후 진행합니다.

## 역할과 호출 흐름

C#은 인증·소유자 검증, 거래 CRUD, 통계 계산, 데이터 버전·캐시·시도 한도, 결과 검증·저장·화면을 담당합니다. 파트너는 Python HTTP API, 요청 계약 검증, 프롬프트·LLM 호출, 출력 계약 검증을 담당합니다. 브라우저는 Python을 직접 호출하지 않고 Python은 DB에 직접 접속하지 않습니다.

C# 통계 스냅샷 완료 → DB 읽기 트랜잭션 종료 → 분석 요청 → 응답 검증 → 성공 결과 저장입니다. 네트워크/LLM 처리 중 DB 트랜잭션이나 거래 행 잠금을 유지하지 않습니다.

## 내부 HTTP API

`POST /internal/v1/reports/monthly`

Content-Type: application/json. Authorization: Bearer 서비스 키. 서비스 키는 서버 환경변수/비밀 저장소로만 관리합니다. 예제에는 실제 키가 없습니다. 서비스는 외부 사용자에게 직접 공개하지 않습니다.

요청 예제: [monthly-request.json](examples/monthly-request.json)

정상 응답 예제: [monthly-response.json](examples/monthly-response.json)

예제는 2026-09 가상 거래와 2026-10-01 기준이며 JSON은 실제 C# 계약 코드에서 생성·검증했습니다.

## 요청 필드

| 필드 | 규칙 |
|---|---|
| contractVersion | 현재 1.0 |
| requestId | UUID, 응답에 동일한 값 반환. 재시도도 동일 값 |
| locale/currency/timeZone | ko-KR / KRW / Asia/Seoul |
| month/asOfDate/dataVersion | 대상 YYYY-MM, 한국 기준 날짜, 거래 버전 |
| isCalendarMonthComplete | 달력상 종료 여부이며 기록 완전성 보장 아님 |
| period | start, endInclusive, days. 종료일 포함 |
| recordingCoverage/recordCount | unknown 및 대상 기간 거래 수 |
| facts | id, value(decimal 또는 null), unit(KRW 또는 percent) |
| categories | code/name/amount/previousAmount/deltaAmount/changeRatePercent/sharePercent |
| comparison | status, mode(full_month/same_day), previousPeriod, differentDayCounts |
| largestExpenses | date/categoryCode/amount만 포함, 동률 모두 제공 |
| limitations | C#이 확정한 제한 문구. 화면에서 항상 표시 |

개인 이름·이메일·사용자 ID·거래 ID·제목·메모는 보내지 않습니다. 카테고리 이름/코드는 공용 서버 기준입니다. facts의 null은 비교 불가이며 0으로 바꾸지 않습니다. 설명 근거로 null 값을 인용하지 않습니다.

현재 월은 한국 기준 오늘까지, 전월도 같은 일자까지(없으면 말일)입니다. 과거 월은 전체 달력 월끼리 비교합니다. 일수가 다르면 자동 보정하지 않습니다. 평균·비율은 decimal/AwayFromZero 소수 두 자리, 수지는 수입−지출이며 실제 잔액·저축액이 아닙니다.

## 응답 규칙

성공 200, contractVersion 및 requestId가 요청과 같아야 합니다. report는 summary 1개, highlights/changesToReview/suggestions 각각 0~3개입니다. 빈 배열을 명시하며 필드를 생략하지 않습니다.

각 항목은 text(공백 제외 1~500자)와 evidenceIds(1~20개)를 포함합니다. 모든 근거 ID는 요청의 값이 null이 아닌 facts에 존재해야 합니다. 알 수 없는 응답 필드, 필수 필드 누락, 다른 requestId/버전, 과도한 출력은 거절합니다. C# 응답 크기 상한은 64 KiB입니다.

근거 ID만으로 문장의 사실성은 보장되지 않습니다. 파트너는 수치·기간이 근거와 일치하도록 출력하고 표본 리포트를 함께 평가해야 합니다. 사용자 성향, 예산 초과, 제공되지 않은 장기 추세를 추정하지 않습니다.

## 오류·시간·중복·비용

- Python 입력/인증 오류는 400/401/403, 일시 장애는 503 등으로 반환합니다. 상세 내부 예외·키를 반환하지 않습니다.
- C#은 정상 응답 이외를 외부 502로 매핑하며 응답 계약 오류도 502입니다. Python 503 또는 네트워크 요청 실패만 최대 1회 재시도합니다. 계약 오류와 시간 초과에는 재시도하지 않습니다.
- 내부 요청당 최대 25초, 전체 리포트 흐름 최대 60초. 시간 초과는 외부 504입니다. 시간 초과 후 Python 처리가 계속될 수 있어 정확히 한 번 호출을 보장하지 않습니다.
- 파트너는 동일 requestId 처리 중/완료를 식별해 중복 비용을 줄이는 동작을 준비해야 합니다. 저장소·유효 기간은 실제 연동 전에 합의합니다.
- 같은 사용자·월 분석이 진행 중이면 C#은 409입니다. 사용자 하루 분석 시도 한도 기본 5회. 새 시도가 시작된 뒤 실패해도 차감하며 재시도는 같은 시도입니다. 캐시 반환/빈 기록 거절은 차감하지 않습니다.
- 캐시 기준은 사용자·월·dataVersion·asOfDate·contractVersion·promptVersion·modelKey입니다. 거래 또는 날짜·설정 변경 시 기존 결과는 갱신 필요입니다. 실패는 기존 성공 결과를 덮어쓰지 않습니다.

현재 동시 요청 가드·한도는 단일 C# 프로세스 메모리이며 재시작하면 초기화됩니다. 다중 인스턴스/운영 비용 통제 전에는 공유 저장소와 공유 잠금을 도입해야 합니다. DB 고유 캐시는 중복 저장을 방지하지만 여러 인스턴스의 중복 외부 호출까지 방지하지는 않습니다.

## C# 실행 설정

기본 설정은 Mock이며 Python 설치 없이 실행됩니다. 서버 환경변수 예:

```powershell
$env:Analysis__Mode = 'Http'
$env:Analysis__BaseUrl = 'http://localhost:5090/'
$env:Analysis__ModelKey = 'partner-monthly-v1'
# Analysis__ServiceKey는 서버 비밀 설정에서 주입합니다. 실제 키를 문서/소스에 쓰지 않습니다.
```

외부 주소는 HTTPS를 사용하며 HTTP는 루프백 주소에만 허용합니다. 리다이렉트를 따르지 않습니다. BaseUrl은 서비스 루트 주소와 마지막 /를 사용하세요. 모드/키/주소 변경 후 웹을 재시작합니다. 모델·프롬프트를 바꾸면 ModelKey/PromptVersion도 변경해 캐시를 분리합니다.

검증: `dotnet run --project tests/AnalysisChecks`. 전체 DB 통합 검증은 scripts/Test-DatabaseFoundation.ps1입니다. 로컬 사용자 화면은 /Reports이며 외부 C# API는 POST /api/analysis/reports, GET /api/analysis/reports/latest?month=YYYY-MM, GET /api/analysis/reports/{id}입니다. 변경 요청은 인증 쿠키와 CSRF가 필요합니다.

## 파트너와 확정할 항목

계약 JSON 필드와 버전, Python 프레임워크/포트/호스팅, 서비스 인증·키 교체, LLM 모델/프롬프트 버전, 동일 requestId 보존 방식, 시간 제한과 실제 서비스 오류 규칙을 확정합니다. 이 초안과 예제는 비밀 값 없이 GitHub에 공유할 수 있습니다.
