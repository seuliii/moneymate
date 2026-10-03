using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MoneyMate.Contracts;
using Microsoft.Extensions.Options;

namespace MoneyMate.Services;

public sealed class AnalysisOptions
{
    public string Mode { get; set; } = "Mock";
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
    public string ModelKey { get; set; } = "partner-monthly-v1";
    public int DailyAttempts { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 25;
}
public sealed class AnalysisFailure(int status, string code) : Exception
{
    public int Status => status;
    public string Code => code;
}
public interface IAnalysisClient { Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken); }

public sealed class MockAnalysisClient : IAnalysisClient
{
    public async Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        var expense = input.Facts.Single(x => x.Id == "expense.total").Value!.Value;
        var summary = new ReportStatement($"등록된 {input.Month} 지출은 {expense:N0}원입니다. 이 내용은 연결 확인용 모의 리포트입니다.", ["expense.total"]);
        var top = input.Categories.OrderByDescending(x => x.Amount).FirstOrDefault(x => x.Amount > 0);
        ReportStatement[] highlights = top is null ? [] : [new($"{top.Name} 지출은 {top.Amount:N0}원입니다.", ["category." + top.Code + ".amount"])];
        return new(input.ContractVersion, input.RequestId, new(summary, highlights, [],
            [new("누락된 거래가 있는지 확인하고 기록을 이어가세요.", ["expense.total"])]));
    }
}

public sealed class HttpAnalysisClient(HttpClient http, IOptions<AnalysisOptions> configured) : IAnalysisClient
{
    public async Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "internal/v1/reports/monthly"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceKey);
                request.Content = JsonContent.Create(input, options: AnalysisContract.Json);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt == 0) continue;
                if (!response.IsSuccessStatusCode) throw new AnalysisFailure(502, "analysis_service_failed");
                // Limit response size before parsing untrusted partner output.
                await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var buffer = new MemoryStream();
                var block = new byte[4096];
                int count;
                while ((count = await source.ReadAsync(block, deadline.Token)) > 0)
                {
                    if (buffer.Length + count > 65_536) throw new AnalysisFailure(502, "invalid_analysis_response");
                    buffer.Write(block, 0, count);
                }
                return JsonSerializer.Deserialize<AnalysisOutput>(buffer.ToArray(), AnalysisContract.Json)
                    ?? throw new AnalysisFailure(502, "invalid_analysis_response");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AnalysisFailure(504, "analysis_timeout"); }
            catch (HttpRequestException) when (attempt == 0) { }
            catch (HttpRequestException) { throw new AnalysisFailure(502, "analysis_service_failed"); }
            catch (JsonException) { throw new AnalysisFailure(502, "invalid_analysis_response"); }
            catch (IOException) { throw new AnalysisFailure(502, "analysis_service_failed"); }
        }
        throw new AnalysisFailure(502, "analysis_service_failed");
    }
}
