# MoneyMate 파트너 분석 API (Python)

C#이 계산한 월 통계를 받아 근거 ID가 붙은 월간 리포트를 반환하는 내부 HTTP 서비스입니다. 계약 기준은 `docs/api/partner-contract-v1.md`입니다. 현재 생성기는 LLM 없이 facts로 문장을 만드는 **규칙 기반 스텁**이며 C#과의 연결 확인용입니다.

- 프레임워크: FastAPI / 포트: 5090 (기획안 후보값)
- Python 3.9 이상
- 엔드포인트: `POST /internal/v1/reports/monthly` (Bearer 서비스 키)

## 실행

`analysis` 폴더에서:

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements-dev.txt
export ANALYSIS_SERVICE_KEY='<로컬 임의 값>'   # 문서·소스에 실제 키를 쓰지 않습니다.
.venv/bin/uvicorn app.main:create_app --factory --host 127.0.0.1 --port 5090
```

Windows PowerShell은 `.venv\Scripts\...`, `$env:ANALYSIS_SERVICE_KEY = '...'`를 사용합니다. `ANALYSIS_SERVICE_KEY`가 없으면 시작하지 않습니다.

예제 요청:

```bash
curl -X POST http://127.0.0.1:5090/internal/v1/reports/monthly \
  -H "Authorization: Bearer $ANALYSIS_SERVICE_KEY" -H 'Content-Type: application/json' \
  --data @../docs/api/examples/monthly-request.json
```

C# 웹을 이 서비스에 연결하려면 웹 서버 환경변수를 설정하고 웹을 재시작합니다. 두 키는 같은 값이어야 합니다.

```powershell
$env:Analysis__Mode = 'Http'
$env:Analysis__BaseUrl = 'http://localhost:5090/'
$env:Analysis__ServiceKey = '<ANALYSIS_SERVICE_KEY와 같은 값>'
```

## 검증

```bash
.venv/bin/python -m pytest
```

저장소 예제 JSON 파싱, 응답 계약 검증(C# `AnalysisContract.Valid`와 같은 기준), 서비스 키 인증, 입력 오류 400, 월·기간 의미 검증, 생성 실패 상태 코드와 로그 비노출, 전월 기록 없음·지출 0원 처리, 진행 중인 월의 비교 문구를 확인합니다.

## 구조

```text
analysis/
  app/
    contracts.py    # 요청/응답 Pydantic 모델 (camelCase, 알 수 없는 필드 거절)
    validation.py   # 응답 계약 검증: 개수·길이(UTF-16)·근거 ID·64 KiB
    generators.py   # ReportGenerator 인터페이스, StubReportGenerator
    main.py         # FastAPI 앱, 인증, 오류 매핑
  tests/
```

## 상태 코드

| 상황 | 상태 | C# 처리 |
|---|---|---|
| 정상 | 200 | 응답 검증 후 저장 |
| 입력 계약 위반 | 400 `invalid_request` | 502, 재시도 없음 |
| 서비스 키 없음/불일치 | 401 `unauthorized` | 502, 재시도 없음 |
| 생성기 일시 장애 (`ServiceError(503, ...)`) | 503 | 같은 requestId로 1회 재시도 |
| 생성기 예외·출력 계약 위반 | 500 | 502, 재시도 없음 |

오류 응답에는 `code`만 포함하고 내부 예외, 요청 통계, 키를 반환하거나 로그에 남기지 않습니다. 생성기 예외는 메시지·스택 없이 `requestId`, 오류 코드, 예외 유형 이름만 기록하고, 출력 계약 위반은 위반 위치만 기록합니다.

입력은 형식 외에 C# `StatisticsCalculator`와 같은 기준으로 의미를 검증하며, 어긋나면 400입니다.

- `month`는 실제 월(01~12)이고 `period.start`는 그 달 1일
- `period.days`는 종료일 포함 일수와 같고 기간은 한 달 안에 있음
- 완료된 월은 말일까지·`full_month`, 진행 중인 월은 `asOfDate`까지·`same_day`
- `previousPeriod`는 전월 1일부터 전체 달(또는 같은 일자, 전월 말일 상한)까지이며 `differentDayCounts`가 일수 비교와 일치
- 비교 가능 상태(`comparable`, `zero_previous_expense`)에는 `previousPeriod`가 있음

스텁 생성기는 진행 중인 월이면 요약과 카테고리 변화 모두 “전월 같은 기간”, 완료된 월이면 “전월”과 비교한다고 표현합니다.

## 다음 단계

1. LLM 생성기: `ReportGenerator`를 구현하고 구조화 출력·프롬프트 버전(`monthly-v1`)을 적용합니다. 모델·프롬프트 변경 시 C# `ModelKey`/`PromptVersion`도 함께 변경합니다.
2. 출력 사실성 검증: 문장 수치·기간과 근거 값 일치, 금지 단정 표현 검사.
3. 운영: 동일 `requestId` 처리 중/완료 식별, 25초 내 응답, 토큰·지연 기록.
