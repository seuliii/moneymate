using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MoneyMate.Contracts;
using MoneyMate.Services;

namespace MoneyMate.Pages.Reports;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(ReportService service, KoreanClock calendar) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Month { get; set; } = "";
    public SavedReport? Saved { get; private set; }
    public string? Message { get; private set; }
    public bool IsMock => service.IsMock;
    public string CurrentMonth => calendar.CurrentMonth;
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public static string EvidenceLabel(string id, AnalysisInput input)
    {
        var labels = new Dictionary<string, string> {
            ["income.total"] = "총 수입", ["expense.total"] = "총 지출", ["net.total"] = "월간 수지",
            ["expense.dailyAverage"] = "일평균 지출", ["comparison.previousExpense"] = "전월 지출",
            ["comparison.delta"] = "전월 대비 증감액", ["comparison.rate"] = "전월 대비 증감률" };
        foreach (var category in input.Categories)
        {
            var prefix = "category." + category.Code;
            labels[prefix + ".amount"] = category.Name + " 지출";
            labels[prefix + ".share"] = category.Name + " 지출 비중";
            labels[prefix + ".delta"] = category.Name + " 증감액";
            labels[prefix + ".rate"] = category.Name + " 증감률";
        }
        var fact = input.Facts.Single(x => x.Id == id);
        var value = fact.Value!.Value;
        return labels.GetValueOrDefault(id, "통계") + " " + value.ToString(value == decimal.Truncate(value) ? "N0" : "N2") + (fact.Unit == "percent" ? "%" : "원");
    }
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Month)) Month = CurrentMonth;
        var latest = await service.LatestAsync(Owner, Month, ct);
        if (latest.Status == 400) return BadRequest(latest.Message ?? "조회 월을 확인해주세요.");
        Saved = latest.Value;
        if (latest.Status == 503) { Message = latest.Message; Response.StatusCode = 503; }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await service.GenerateAsync(Owner, Month, ct);
        if (result.Status is 200 or 201)
        {
            TempData["ReportMessage"] = result.Status == 200 ? "저장된 리포트를 다시 사용했습니다." : "리포트를 생성했습니다.";
            return RedirectToPage(new { month = Month });
        }
        if (result.Status == 400 && result.Code != "no_records") return BadRequest(result.Message ?? "조회 월을 확인해주세요.");
        Message = result.Message;
        Response.StatusCode = result.Status;
        Saved = (await service.LatestAsync(Owner, Month, ct)).Value;
        return Page();
    }
}

