# 2단계 개발 및 검증 기록

작성일: 2026-10-03 (한국 기준).

## 설계와 구현

기존 Identity 테이블과 Razor Pages/Controller 구조를 유지했다. AccountService가 회원가입·로그인·로그아웃·현재 사용자 조회를 담당하며 화면과 API가 같은 서비스를 사용한다. 사용자 생성과 UserLedgerState(DataVersion=0) 저장은 같은 DB 트랜잭션으로 처리한다.

화면: 회원가입, 로그인, 내 계정, POST 로그아웃, 접근 거절 안내. 공통 헤더는 인증 상태에 따라 로그인/회원가입 또는 내 계정/로그아웃 메뉴를 보여준다. 홈의 가입 버튼도 실제 회원 화면으로 연결했다.

API: /api/auth/csrf, register, login, logout, me. 사용자 ID를 클라이언트에서 받지 않고 Identity 사용자 식별자로 내 정보를 조회한다.

보안: Identity 비밀번호 해싱, 이메일 중복 및 비밀번호 정책, 5회 실패 후 5분 잠금, IP당 30회/분 요청 제한, HttpOnly 인증 쿠키, 운영 Secure, 모든 POST의 CSRF 검증, HTML 인코딩, 로컬 ReturnUrl 검증.

초기 스키마가 필요한 테이블을 이미 포함하므로 이번 단계의 추가 마이그레이션은 없다. 실제 개발 DB에 테스트 계정을 생성하지 않고 격리 DB에서 검증했다.

## 비밀번호 파일 제외

MoneyMate.csproj에서 appsettings.Local.json의 CopyToOutputDirectory와 CopyToPublishDirectory를 Never로 변경했다. 로컬 개발 시 원본 설정은 그대로 읽는다. 이전 Debug 출력 폴더의 해당 파일 복사본은 제거했다.

MSBuild 콘텐츠 항목에서 Never/Never를 확인했고, artifacts/publish-stage2에 게시한 결과 해당 파일이 포함되지 않았다. 실제 앱 DB 접속 정보는 출력하지 않았다.

## 검증 결과

- 빌드: 경고 0 / 오류 0.
- Debug 게시 성공, 비밀번호 로컬 설정 파일 미포함.
- 격리 PostgreSQL에 초기 마이그레이션 적용 성공, 모델 스냅샷 차이 없음.
- API 회원가입과 폼 회원가입 성공, 가입 직후 미로그인 상태 확인.
- 대소문자만 다른 이메일 가입 거절, 약한 비밀번호 거절.
- 정상 로그인, 내 정보 조회, 두 사용자 세션 분리, 다른 사용자 ID를 쿼리에 넣어도 자신의 정보만 반환.
- CSRF 없는 가입·로그인·로그아웃 요청 모두 거절.
- GET 로그아웃은 세션을 종료하지 않으며 POST 로그아웃 후 미인증 401.
- 사용자 A 로그아웃 후 B 로그인 세션 유지.
- 스크립트 문자열 이름을 내 정보 화면에서 HTML 인코딩해 표시.
- 외부 ReturnUrl 대신 내 계정으로 이동.
- HttpOnly 세션 인증 쿠키 확인.
- 비밀번호 오류 누적 후 잠금, 정상 비밀번호도 잠금 기간에 거절.
- 요청 횟수 제한 초과 시 429.
- 등록한 세 사용자에 UserLedgerState가 생성되고 비밀번호가 해시로 저장됨.
- 기존 카테고리 17개, 거래 금액·유형·소유자 제약 및 DB 장애 시 웹 생존 검증 통과.

기존 웹 실행 프로세스가 빌드 파일을 잠가 최초 빌드는 중단되었으나, 해당 개발 서버를 종료한 후 재빌드가 경고·오류 없이 통과했다.

## 범위와 다음 단계

회원·인증 기반은 구현했으며 거래별 소유권 검증은 3단계 거래 API에서 적용한다. 이메일 인증/비밀번호 재설정/탈퇴는 아직 제공하지 않는다. 다중 인스턴스 배포 시 요청 제한 공유, Data Protection 키 공유, 프록시 구성 등 운영 설정을 추가한다.

3단계: 거래 입력·목록·수정·삭제, 카테고리 조회, 소유자 검증, 거래 버전/사용자 데이터 버전의 원자적 증가.

## 사용자 확인용 계정 및 재검증 — 2026-10-03

사용자 요청으로 2단계 통합 검증을 다시 수행했다. 빌드는 경고·오류 없이 통과했으며, 격리 DB의 회원/보안/DB 기반 검증도 모두 통과했다.

사용자가 직접 로그인할 수 있도록 실제 로컬 개발 DB에 `demo@moneymate.local` 계정을 생성했다. 기존 계정의 비밀번호를 변경하지 않았다. 확인용 계정으로 가입 201, 로그인 200, 내 계정 화면 200, 로그아웃 204, 로그아웃 후 내 정보 요청 401을 확인했다. 개발 DB 상태는 ready다.

이 계정은 사용자의 로컬 확인을 위한 계정이며 테스트용 임시 DB 계정과 구분된다. 자동 생성/하드코딩된 운영 계정은 아니다. 비밀번호는 사용자에게 채팅으로 전달하고 소스와 문서에는 저장하지 않는다. 계정은 로컬 DB에 유지된다.

로그인 주소: http://localhost:5077/Account/Login. 로그인 후 내 정보 주소: http://localhost:5077/Account.

참고: [Identity 설정](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [CSRF 보호](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [API 인증 상태 코드](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0).
