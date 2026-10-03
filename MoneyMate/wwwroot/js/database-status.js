const checkButton = document.getElementById('db-check');
const result = document.getElementById('db-result');
checkButton.addEventListener('click', async () => {
    checkButton.disabled = true;
    result.textContent = 'DB 연결을 확인하고 있습니다…';
    result.dataset.status = 'loading';
    try {
        const response = await fetch('/api/development/database-status', {
            signal: AbortSignal.timeout(8000), cache: 'no-store'
        });
        if (!response.ok) throw new Error('Status check failed');
        const status = await response.json();
        result.textContent = status.message;
        result.dataset.status = status.status;
    } catch {
        result.textContent = '연결 상태를 확인하지 못했습니다. 서버 실행 상태를 확인해주세요.';
        result.dataset.status = 'unavailable';
    } finally {
        checkButton.disabled = false;
    }
});
