# MoneyMate 파트너 분석 API (Python)

C#이 계산한 월 통계를 받아 근거 ID가 붙은 월간 리포트를 반환하는 내부 HTTP 서비스입니다. 계약 기준은 `docs/api/partner-contract-v1.md`입니다. 생성기는 두 가지입니다. 기본값은 LLM 없이 facts로 문장을 만드는 **규칙 기반 스텁**(C# 연결 확인용)이고, `ANALYSIS_GENERATOR=llm`이면 **Claude 생성기**를 사용합니다.

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

### Claude 생성기 사용

```bash
export ANALYSIS_GENERATOR=llm
export ANTHROPIC_API_KEY='<Anthropic API 키>'   # 서버 비밀 설정으로만 주입합니다.
# 선택: export ANALYSIS_LLM_MODEL=claude-opus-5-5  ANALYSIS_LLM_EFFORT=low
```

| 환경변수 | 기본값 | 설명 |
|---|---|---|
| `ANALYSIS_GENERATOR` | `stub` | `stub` 또는 `llm`. 그 밖의 값이면 시작하지 않습니다. |
| `ANALYSIS_LLM_MODEL` | `claude-opus-5-5` | 바꾸면 C# `Analysis__ModelKey`도 함께 바꿔 캐시를 분리합니다. |
| `ANALYSIS_LLM_EFFORT` | `low` | `low`/`medium`/`high`. 높일수록 품질이 오를 수 있지만 느려집니다. 20초 제한 안에서 조정합니다. |

동작 방식(`app/llm.py`):

- 요청 통계 중 문장 작성에 필요한 필드만 JSON으로 보내고(`requestId`·`dataVersion` 제외), 인용 가능한 근거 ID 목록과 비교 기준 표현(“전월”/“전월 같은 기간”)을 함께 줍니다.
- 시스템 프롬프트(`monthly-v1`)는 facts 값만 사용하고, 근거 ID를 인용하며, null을 비교하지 않고, 성향·예산·장기 추세를 추정하지 않도록 지시합니다.
- 구조화 출력(JSON 스키마)으로 응답을 받은 뒤 기존 응답 계약 검증을 그대로 거칩니다.
- 요청당 20초 제한이고 SDK 자체 재시도는 하지 않습니다(C# 25초 제한·1회 재시도와 맞춤). 안전 분류기가 거절하면 서버 측 `fallbacks: "default"`로 다른 모델이 이어서 처리합니다.
- 로그에는 `requestId`, 프롬프트 버전, 응답 모델, 종료 사유, 입력·출력 토큰, 지연(ms)만 남깁니다. 요청 통계와 생성 문장은 남기지 않습니다.

## 검증

```bash
.venv/bin/python -m pytest
```

저장소 예제 JSON 파싱, 응답 계약 검증(C# `AnalysisContract.Valid`와 같은 기준), 서비스 키 인증, 입력 오류 400, 월·기간 의미 검증, 생성 실패 상태 코드와 로그 비노출, 전월 기록 없음·지출 0원 처리, 진행 중인 월의 비교 문구를 확인합니다. Claude 생성기 테스트는 실제 SDK로 요청을 직렬화하되 네트워크 대신 모의 응답을 사용하므로 API 키와 비용이 들지 않습니다(요청 형식, 오류→상태 코드, 거절·잘림·잘못된 JSON, 로그 비노출).

## 구조

```text
analysis/
  app/
    contracts.py    # 요청/응답 Pydantic 모델 (camelCase, 알 수 없는 필드 거절)
    validation.py   # 응답 계약 검증: 개수·길이(UTF-16)·근거 ID·64 KiB
    generators.py   # ReportGenerator 인터페이스, StubReportGenerator
    llm.py          # LlmReportGenerator: Claude 호출, 프롬프트(monthly-v1), 오류 매핑
    errors.py       # ServiceError(status, code)
    main.py         # FastAPI 앱, 인증, 생성기 선택, 오류 매핑
  tests/
```

## 상태 코드

| 상황 | 상태 | C# 처리 |
|---|---|---|
| 정상 | 200 | 응답 검증 후 저장 |
| 입력 계약 위반 | 400 `invalid_request` | 502, 재시도 없음 |
| 서비스 키 없음/불일치 | 401 `unauthorized` | 502, 재시도 없음 |
| 생성기 일시 장애: LLM 연결 실패·시간 초과·408/409/429/5xx (`llm_unavailable`) | 503 | 같은 requestId로 1회 재시도 |
| LLM 요청 오류(400/401/403/404 등, `llm_request_failed`), 거절(`llm_refused`), 잘림·잘못된 JSON(`llm_invalid_output`) | 500 | 502, 재시도 없음 |
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

1. 실제 API 키로 표본 리포트를 생성해 품질과 지연을 평가하고 `ANALYSIS_LLM_EFFORT`를 정합니다. 모델·프롬프트 변경 시 C# `ModelKey`/`PromptVersion`도 함께 변경합니다.
2. 출력 사실성 검증: 문장 수치·기간과 근거 값 일치, 금지 단정 표현 검사.
3. 운영: 동일 `requestId` 처리 중/완료 식별.
