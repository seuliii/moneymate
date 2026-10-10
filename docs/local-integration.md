# C# · Python 로컬 통합 테스트

## 준비

Windows에서 .NET 10 SDK, Python 3.9 이상, PostgreSQL이 필요하다. 프로젝트 DB와 마이그레이션은 [README](../README.md)의 최초 설정을 따른다. 아래 명령은 `MoneyMateDev` 폴더에서 실행한다.

Python 가상환경·패키지는 최초 한 번 준비한다. Python 실행 명령이 `py`가 아닌 PC에서는 설치된 Python 명령으로 바꾼다.

```powershell
py -m venv .\analysis\.venv
.\analysis\.venv\Scripts\python.exe -m pip install -r .\analysis\requirements-dev.txt
```

이미 준비된 가상환경을 다시 만들 필요는 없다. 의존성 파일이 변경되면 패키지 설치 명령을 다시 실행한다.

## 실행과 종료

```powershell
cd C:\Users\job62\Desktop\MoneyMate\MoneyMateDev
powershell -NoProfile -File .\scripts\Start-LocalIntegration.ps1
```

실행 정책으로 차단되는 개인 개발 PC에서는 스크립트 내용을 확인한 뒤 해당 실행에만 적용한다. 조직에서 강제하는 정책은 우회하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-LocalIntegration.ps1
```

- 웹: http://localhost:5077/Account/Login
- Python API 문서: http://127.0.0.1:5090/docs
- 종료: 실행한 터미널에서 **Ctrl+C**. 두 서비스를 함께 종료한다.

스크립트는 C# 빌드, 공유 인증키 임의 생성, HTTP 모드 실행, 서비스·DB 상태 확인을 수행한다. 키는 두 자식 프로세스 환경에만 전달하며 파일·출력에 기록하지 않는다. 부모 프로세스의 기존 환경변수는 복원한다. DB 생성·마이그레이션·테스트 계정 생성은 수행하지 않는다.

현재 Python 생성기는 **규칙 기반 스텁**이다. HTTP 모드라는 이유만으로 실제 AI 분석이 되는 것은 아니다. `partner-stub-local-v1` 모델 키를 사용해 향후 실제 AI 리포트 캐시와 구분한다. 실제 모델로 전환할 때는 파트너와 설정·모델 키를 다시 맞춘다.

실행 로그는 Git 제외 폴더 `artifacts/local-integration/run-*/`에 저장한다. 로그를 공유하기 전 개인정보·설정 값 포함 여부를 확인한다.

기존 서비스가 포트를 사용 중이면 중단하며, 기존 프로세스를 자동 종료하지 않는다. 기존 실행을 종료하거나 별도 포트를 지정한다.

```powershell
powershell -NoProfile -File .\scripts\Start-LocalIntegration.ps1 -WebPort 5087 -AnalysisPort 5091
```

준비된 빌드 산출물을 그대로 테스트할 때만 `-SkipBuild`를 추가한다. 이 경우 수정된 C# 코드는 자동 반영되지 않는다.

## 사용자 테스트 순서

1. 회원가입 후 로그인한다. 기존 로컬 계정도 사용할 수 있다.
2. 거래 목록에서 이번 달 수입·지출과 전월 같은 기간의 지출을 등록한다.
3. 대시보드에서 등록 금액과 월별 비교를 확인한다.
4. 리포트에서 같은 달을 선택해 생성 버튼을 누른다. Python 결과가 저장되어 표시되는지 확인한다.
5. 거래를 변경하지 않고 다시 생성한다. 저장된 리포트를 재사용한다.
6. 거래 금액을 수정한다. 재분석 필요 표시를 확인하고 다시 생성해 결과가 갱신되는지 확인한다.

Python 로그의 `POST /internal/v1/reports/monthly` 성공 기록으로 실제 서비스 호출을 확인할 수 있다. 캐시를 재사용하면 Python을 다시 호출하지 않는다. 계정별 하루 생성 시도 한도가 있으며 기본값은 5회다.

Python API는 서비스 인증키가 필요하므로 API 문서에서 인증 없이 직접 생성 요청을 보내면 401이다. 일반 사용자는 웹 화면으로 테스트한다. 실제 비밀번호와 서비스 키는 저장소·Wiki에 올리지 않는다.

## 앞으로의 단계

| 단계 | 범위 | 완료 기준 |
|---|---|---|
| 6. 로컬 통합 실행 | 공동 실행 스크립트·사용자 테스트 안내·연동 검증 기록 | 두 서비스 실행 및 종료, DB 확인, HTTP 리포트 생성·캐시 확인 |
| 7. 화면·오류 안내 점검 | 좁은 화면, 빈 데이터, 입력 오류, 분석 서비스 장애·재시도 안내, 스텁/실제 AI 구분 | 사용자 테스트와 발견한 문제 수정, 회귀 검증 |
| 8. 배포 준비 | 게시 산출물, 운영 설정·비밀, HTTPS, DB 백업·복원, Data Protection, 단일 인스턴스 운영 조건 | 배포 대상에 맞춘 설정·검증·실행 문서 |
| 파트너와 공동 검증 | 실제 AI 생성기 연결, 수치·근거 사실성, 시간 초과·실패 시 기존 결과 유지 | 파트너 구현 반영 후 실제 모델로 통합 검증 |

배포 대상은 8단계 시작 전에 결정한다. 실제 AI가 없는 상태로 공개할 경우 스텁임을 화면에서 명확히 안내해야 한다.
