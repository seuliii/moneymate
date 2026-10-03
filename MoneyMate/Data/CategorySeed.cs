using MoneyMate.Models;

namespace MoneyMate.Data;

internal static class CategorySeed
{
    public static readonly Category[] All =
    [
        Create(1, "expense_food", "식비", TransactionType.Expense),
        Create(2, "expense_cafe", "카페", TransactionType.Expense),
        Create(3, "expense_transport", "교통", TransactionType.Expense),
        Create(4, "expense_shopping", "쇼핑", TransactionType.Expense),
        Create(5, "expense_housing", "주거", TransactionType.Expense),
        Create(6, "expense_living", "생활", TransactionType.Expense),
        Create(7, "expense_leisure", "문화/여가", TransactionType.Expense),
        Create(8, "expense_subscription", "구독", TransactionType.Expense),
        Create(9, "expense_health", "의료", TransactionType.Expense),
        Create(10, "expense_education", "교육", TransactionType.Expense),
        Create(11, "expense_occasions", "경조사", TransactionType.Expense),
        Create(12, "expense_other", "기타", TransactionType.Expense),
        Create(101, "income_salary", "급여", TransactionType.Income),
        Create(102, "income_bonus", "상여금", TransactionType.Income),
        Create(103, "income_side", "부수입", TransactionType.Income),
        Create(104, "income_allowance", "용돈", TransactionType.Income),
        Create(105, "income_other", "기타", TransactionType.Income)
    ];

    private static Category Create(int id, string code, string name, TransactionType type) =>
        new() { Id = id, Code = code, Name = name, Type = type, SortOrder = id };
}
