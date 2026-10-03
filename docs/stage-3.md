# 3단계 — 사용자별 거래 관리

상태: 구현/검증 완료. [PR #2](https://github.com/seuliii/moneymate/pull/2)로 dev에 반영했다.

- 사용자별 거래 등록·조회·수정·삭제, 월/유형/카테고리 필터와 페이지 이동을 제공한다.
- 양의 원화 정수(최대10억 원), 유형별 카테고리, 한국 기준 미래 날짜 제한, 제목/메모 길이를 검증한다.
- 소유자는 인증 정보에서 결정한다. 타인 거래는404, 변경 요청은 인증·CSRF를 요구한다.
- 오래된 거래 version의 수정/삭제는409로 거절한다. 사용자 버전 행을 잠근 짧은 트랜잭션에서 거래와 두 버전을 함께 갱신한다.
- 마지막 페이지를 넘는 조회는 보정하며 삭제는 확인 화면의 POST로만 수행한다. 기존 스키마를 사용한다.

화면: /Transactions. API: /api/transactions, /api/categories.

[검증 기록](verification.md) · [API/실행 방법](../README.md) · [현재 상태/남은 작업](development-status.md)
