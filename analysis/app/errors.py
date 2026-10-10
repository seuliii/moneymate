"""서비스 오류. 응답에는 code만 담고 C#은 status로 재시도 여부를 판단한다(503만 1회 재시도)."""


class ServiceError(Exception):
    def __init__(self, status: int, code: str) -> None:
        super().__init__(code)
        self.status = status
        self.code = code
