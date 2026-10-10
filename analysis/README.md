# MoneyMate 파트너 분석 API (Python)

C#이 계산한 월 통계를 받아 근거 ID가 붙은 월간 리포트를 반환하는 내부 HTTP 서비스입니다. 계약 기준은 `docs/api/partner-contract-v1.md`입니다. 생성기는 `ANALYSIS_GENERATOR`로 고릅니다. 기본값 `stub`은 LLM 없이 facts로 문장을 만드는 **규칙 기반 스텁**(C# 연결 확인용), `ollama`는 **로컬 오픈 모델**(무료), `claude`는 **Claude API**(유료)입니다.

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

### LLM 생성기 사용

공통: 프롬프트(`monthly-v1`)·출력 스키마·응답 변환은 `app/prompt.py`에 있고 두 생성기가 같이 씁니다.

- 요청 통계 중 문장 작성에 필요한 필드만 보내고(`requestId`·`dataVersion` 제외, facts는 `{id: 값}`으로 축약), 인용 가능한 근거 ID 목록과 비교 기준 표현(“전월”/“전월 같은 기간”)을 함께 줍니다.
- 프롬프트는 facts 값만 사용하고, 근거 ID를 인용하며, 비교 불가 상황에서 증감을 말하지 않고, 성향·예산·장기 추세를 언급하지 않도록 지시합니다. 섹션당 최대 2개의 짧은 문장을 요청합니다.
- JSON 스키마 출력으로 받은 뒤 기존 응답 계약 검증을 그대로 거칩니다. 근거 ID가 틀리면 500 `invalid_report_output`입니다.
- 요청당 20초 제한이며 자체 재시도는 하지 않습니다(C# 25초 제한·1회 재시도와 맞춤).
- 로그에는 `requestId`, 프롬프트 버전, 모델, 종료 사유, 입력·출력 토큰, 지연(ms)만 남깁니다.

| 환경변수 | 기본값 | 설명 |
|---|---|---|
| `ANALYSIS_GENERATOR` | `stub` | `stub`/`ollama`/`claude`. 그 밖의 값이면 시작하지 않습니다. |
| `ANALYSIS_LLM_MODEL` | `qwen2.5:7b`(ollama), `claude-opus-5-5`(claude) | 바꾸면 C# `Analysis__ModelKey`도 함께 바꿔 캐시를 분리합니다. |
| `ANALYSIS_OLLAMA_URL` | `http://127.0.0.1:11434` | Ollama 서버 주소 |
| `ANALYSIS_LLM_EFFORT` | `low` | claude 전용. 높일수록 느려집니다. |

**Ollama (무료, 로컬)**

```bash
brew install ollama          # Windows는 https://ollama.com 설치 파일
ollama serve                 # 다른 터미널에서 계속 실행
ollama pull qwen2.5:7b
export ANALYSIS_GENERATOR=ollama
```

통계가 PC/서버 밖으로 나가지 않습니다. 온도 0으로 같은 통계에 같은 결과를 내고, 모델을 30분간 메모리에 유지합니다.

실측(Apple M4·RAM 24GB, 가상 예제 3종, `tools/sample_reports.py`):

| 모델 | 지연 | 계약 검증 | 메모 |
|---|---|---|---|
| `qwen2.5:7b` | 18~21초 | 3/3 통과 | 20초 제한 경계. 가끔 “예산” 등 금지 표현 |
| `gemma3:12b` | 34~52초 | 2/3 통과 | 문장 품질은 더 좋지만 제한 시간 초과 |

로컬 모델의 지연은 하드웨어(메모리 대역폭)에 좌우됩니다. 배포 서버에서 다시 측정해야 합니다.

**Claude (유료)**: `ANALYSIS_GENERATOR=claude`와 `ANTHROPIC_API_KEY`를 서버 비밀 설정으로 주입합니다. 안전 분류기가 거절하면 서버 측 `fallbacks: "default"`로 다른 모델이 이어서 처리합니다.

**표본 확인**: 가상 예제(완료된 월·진행 중인 월·전월 기록 없음)로 실제 모델을 호출해 문장·계약 검증·지연을 출력합니다.

```bash
.venv/bin/python -m tools.sample_reports --model qwen2.5:7b
```

## 검증

```bash
.venv/bin/python -m pytest
```

저장소 예제 JSON 파싱, 응답 계약 검증(C# `AnalysisContract.Valid`와 같은 기준), 서비스 키 인증, 입력 오류 400, 월·기간 의미 검증, 생성 실패 상태 코드와 로그 비노출, 전월 기록 없음·지출 0원 처리, 진행 중인 월의 비교 문구를 확인합니다. LLM 생성기 테스트는 실제 요청 형식을 만들되 네트워크 대신 모의 응답을 사용하므로 Ollama·API 키 없이 실행됩니다(요청 형식, 오류→상태 코드, 거절·잘림·잘못된 JSON, 로그 비노출).

## 구조

```text
analysis/
  app/
    contracts.py    # 요청/응답 Pydantic 모델 (camelCase, 알 수 없는 필드 거절)
    validation.py   # 응답 계약 검증: 개수·길이(UTF-16)·근거 ID·64 KiB
    generators.py   # ReportGenerator 인터페이스, StubReportGenerator
    prompt.py       # LLM 공통: 프롬프트(monthly-v1), 출력 스키마, 응답 변환
    ollama.py       # OllamaReportGenerator: 로컬 모델 호출, 오류 매핑
    claude.py       # ClaudeReportGenerator: Claude API 호출, 오류 매핑
    errors.py       # ServiceError(status, code)
    main.py         # FastAPI 앱, 인증, 생성기 선택, 오류 매핑
  tests/
  tools/
    sample_reports.py  # 실제 모델로 표본 리포트 생성·검증·지연 측정
```

## 상태 코드

| 상황 | 상태 | C# 처리 |
|---|---|---|
| 정상 | 200 | 응답 검증 후 저장 |
| 입력 계약 위반 | 400 `invalid_request` | 502, 재시도 없음 |
| 서비스 키 없음/불일치 | 401 `unauthorized` | 502, 재시도 없음 |
| 생성기 일시 장애: LLM 연결 실패·시간 초과·5xx(Claude는 408/409/429 포함) (`llm_unavailable`) | 503 | 같은 requestId로 1회 재시도 |
| LLM 요청 오류(400/401/403/404, Ollama 모델 미설치 등, `llm_request_failed`), 거절(`llm_refused`), 잘림·잘못된 JSON(`llm_invalid_output`) | 500 | 502, 재시도 없음 |
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

1. 지연 대응 결정: 로컬 모델이 20초 제한 경계/초과이므로 더 작은 모델, C# 시간 제한 조정, 배포 서버 사양 중 하나를 정합니다. 모델·프롬프트 변경 시 C# `ModelKey`/`PromptVersion`도 함께 변경합니다.
2. 출력 사실성 검증: 문장 수치·기간과 근거 값 일치, 금지 단정 표현 검사.
3. 운영: 동일 `requestId` 처리 중/완료 식별.
