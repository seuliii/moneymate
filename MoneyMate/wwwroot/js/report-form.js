document.querySelector('.report-request')?.addEventListener('submit', event => {
    const button = event.currentTarget.querySelector('button');
    button.disabled = true;
    button.textContent = '분석 중…';
});
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
