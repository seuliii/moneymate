const transactionType = document.getElementById('transaction-type');
const transactionCategory = document.getElementById('transaction-category');
function updateTransactionCategories() {
    for (const option of transactionCategory.options) {
        option.hidden = option.dataset.type !== transactionType.value;
        option.disabled = option.hidden;
    }
    if (transactionCategory.selectedOptions[0]?.disabled || !transactionCategory.value) {
        const first = [...transactionCategory.options].find(option => !option.disabled);
        transactionCategory.value = first?.value ?? '';
    }
}
transactionType.addEventListener('change', updateTransactionCategories);
updateTransactionCategories();
