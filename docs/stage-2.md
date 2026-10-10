# 2단계 — 회원 인증·계정

상태: 구현/검증 완료. [PR #1](https://github.com/seuliii/moneymate/pull/1)로 dev에 반영했다.

- 회원가입·로그인·POST 로그아웃·내 계정 API와 Razor 화면을 제공한다.
- API와 화면은 AccountService를 공유한다. 사용자와 UserLedgerState 생성은 같은 DB 트랜잭션이다.
- Identity 해싱·이메일 중복 검증·8자 이상 비밀번호 정책, 5회 실패 후 5분 잠금과 IP당 30회/분 제한을 적용한다.
- HttpOnly 인증 쿠키, 운영 Secure, CSRF·HTML 인코딩·로컬 ReturnUrl 검증을 적용한다.
- 로컬 비밀번호 설정은 Git에서 제외하고 빌드/게시 복사를 금지한다. 초기 스키마를 사용해 추가 마이그레이션은 없다.

화면: /Account/Register, /Account/Login, /Account. API: /api/auth/csrf, register, login, logout, me.

[검증 기록](verification.md) · [실행 방법](../README.md) · [현재 상태/남은 작업](development-status.md)
