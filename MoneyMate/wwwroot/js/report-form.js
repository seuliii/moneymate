document.querySelector('.report-request')?.addEventListener('submit', event => {
    const button = event.currentTarget.querySelector('button');
    button.disabled = true;
    button.textContent = '분석 중…';
    event.currentTarget.setAttribute('aria-busy', 'true');
    const status = document.getElementById('report-progress');
    if (status) status.textContent = '리포트를 생성하고 있습니다. 잠시 기다려주세요. 실패한 경우 기존 리포트는 유지됩니다.';
});
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
