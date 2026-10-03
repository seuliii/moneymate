# 4단계 — 월별 통계·대시보드

상태: 구현/검증 완료. [PR #3](https://github.com/seuliii/moneymate/pull/3)로 dev에 반영했다.

- 수입·지출·월간 수지·일평균, 카테고리 금액/비중, 최다 카테고리·최대 거래, 전월 비교를 제공한다.
- StatisticsService는 집계와 사용자 데이터 버전을 동일 RepeatableRead 스냅샷에서 조회하고 StatisticsCalculator가 계산한다.
- 현재 월은 한국 기준 오늘까지와 전월 같은 일자까지, 과거 월은 전체 월끼리 비교한다. 전월에 해당 날짜가 없으면 말일로 제한한다.
- 평균은 대상 달력 일수로 나눈다. 평균/비율은 decimal, 소수 둘째 자리 AwayFromZero 반올림이다. 수지는 수입−지출이며 실제 잔액/저축액이 아니다.
- 기록 없음과 지출0을 구분하고 계산 불가 비율은 null이다. 동률을 모두 반환하고 이번/전월 카테고리 합집합으로 비교한다.
- DB 장애 시 API/대시보드는503 안내를 제공한다. 홈은 금액 대신 안내를 표시해 장애를 0원으로 표현하지 않는다.

화면: /Dashboard 및 로그인한 홈. API: /api/statistics/monthly. month 생략은 현재 한국 월, 미래/잘못된 월은400이다. 추가 마이그레이션은 없다.

[검증 기록](verification.md) · [API/실행 방법](../README.md) · [현재 상태/남은 작업](development-status.md)
