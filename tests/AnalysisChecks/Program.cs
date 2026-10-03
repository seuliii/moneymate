using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoneyMate.Contracts;
using MoneyMate.Services;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var stats = new MonthlyStatistics("2026-09", new(2026,10,1), false, new(new(2026,9,1),new(2026,9,30),30),9,5,3500000,154500,3345500,5150,
    [new(1,"식비",12000,7.77m,12000,0,0),new(2,"카페",5500,3.56m,1000,4500,450),new(4,"쇼핑",120000,77.67m,90000,30000,33.33m),new(8,"구독",17000,11,17000,0,0)],
    [new(4,"쇼핑",120000)],[new(Guid.NewGuid(),"Private transaction title",new(2026,9,5),120000,"expense_shopping")],
    new("comparable",new(new(2026,8,1),new(2026,8,31),31),4,120000,34500,28.75m,true));
var input = AnalysisContract.Build(stats,new Dictionary<int,string>{{1,"expense_food"},{2,"expense_cafe"},{4,"expense_shopping"},{8,"expense_subscription"}})
    with { RequestId = Guid.Parse("00000000-0000-4000-8000-000000000001") };
var output = new AnalysisOutput("1.0",input.RequestId,new(new("등록된 9월 지출은 154,500원으로 전월보다 34,500원 증가했습니다.",["expense.total","comparison.delta"]),
    [new("쇼핑 지출은 120,000원으로 전체 지출의 약 77.7%입니다.",["category.expense_shopping.amount","category.expense_shopping.share"])],
    [new("쇼핑 지출이 전월보다 30,000원 증가했습니다. 일회성 구매인지 확인해볼 수 있습니다.",["category.expense_shopping.delta"])],
    [new("다음 달 쇼핑 예정 항목을 기록해보세요.",["category.expense_shopping.amount"])]));
var serialized = JsonSerializer.Serialize(input,AnalysisContract.Json);
Check(!serialized.Contains("Private") && !serialized.Contains("title") && !serialized.Contains("userId") && input.Facts.Count==23,"Private fields/facts");
Check(AnalysisContract.Valid(output,input),"Valid output rejected");
Check(!AnalysisContract.Valid(output with {RequestId=Guid.NewGuid()},input),"Request ID mismatch");
Check(!AnalysisContract.Valid(output with {ContractVersion="2.0"},input),"Version mismatch");
Check(!AnalysisContract.Valid(output with {Report=output.Report with {Summary=new("Unknown",["not.in.facts"])}},input),"Unknown evidence");
Check(!AnalysisContract.Valid(output with {Report=output.Report with {Summary=new(new string('x',501),["expense.total"])}},input),"Text length");
Check(!AnalysisContract.Valid(output with {Report=output.Report with {Suggestions=[output.Report.Summary,output.Report.Summary,output.Report.Summary,output.Report.Summary]}},input),"Section limit");
Check(!AnalysisContract.Valid(output with {Report=output.Report with {Summary=new("Missing evidence",[])}},input),"Missing evidence");
var unavailableFact = input with {Facts=[new("expense.total",null,"KRW")]};
Check(!AnalysisContract.Valid(output,unavailableFact),"Null-valued evidence");
var guard = new AnalysisGuard();
Check(guard.Enter("a:09")&&!guard.Enter("a:09")&&guard.Enter("b:09"),"Owner-month guard");
guard.Exit("a:09"); Check(guard.Enter("a:09"),"Guard release");
Check(guard.Charge("a",new(2026,10,1),1)&&!guard.Charge("a",new(2026,10,1),1)&&guard.Charge("a",new(2026,10,2),1),"Daily quota/reset");
var jsonOutput = JsonSerializer.Serialize(output,AnalysisContract.Json);
var configured = Options.Create(new AnalysisOptions {Mode="Http",BaseUrl="http://localhost:5090/",ServiceKey="fictional-test-key",TimeoutSeconds=1});
var sentIds = new List<Guid>();
var handler = new FakeHandler(async (request, attempt, ct) => {
    Check(request.RequestUri!.AbsolutePath=="/internal/v1/reports/monthly" && request.Headers.Authorization?.ToString()=="Bearer fictional-test-key","HTTP contract path/auth");
    var sent = JsonSerializer.Deserialize<AnalysisInput>(await request.Content!.ReadAsStringAsync(ct),AnalysisContract.Json)!;
    sentIds.Add(sent.RequestId);
    return new HttpResponseMessage(attempt==1?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(jsonOutput,Encoding.UTF8,"application/json")};
});
using (var http = new HttpClient(handler)) {
    var received = await new HttpAnalysisClient(http,configured).AnalyzeAsync(input,CancellationToken.None);
    Check(AnalysisContract.Valid(received,input)&&sentIds.Count==2&&sentIds.Distinct().Count()==1,"503 retry preserves request ID");
}
async Task Fail(string content, string code) {
    using var http = new HttpClient(new FakeHandler((_,_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(content)})));
    try { await new HttpAnalysisClient(http,configured).AnalyzeAsync(input,CancellationToken.None); throw new Exception("Invalid response accepted"); }
    catch(AnalysisFailure ex) { Check(ex.Code==code,"Failure code"); }
}
await Fail("{}","invalid_analysis_response");
await Fail(jsonOutput[..^1]+",\"unexpected\":true}","invalid_analysis_response");
await Fail(new string('x',65537),"invalid_analysis_response");
var timeoutHandler = new FakeHandler(async (_,_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception();});
using(var http=new HttpClient(timeoutHandler)) {
    try { await new HttpAnalysisClient(http,configured).AnalyzeAsync(input,CancellationToken.None); throw new Exception("Timeout not applied"); }
    catch(AnalysisFailure ex) { Check(ex.Status==504&&timeoutHandler.Calls==1,"Timeout retried"); }
}
var mock = await new MockAnalysisClient().AnalyzeAsync(input,CancellationToken.None);
Check(AnalysisContract.Valid(mock,input),"Mock contract");
if(args.Length==1) {
    Directory.CreateDirectory(args[0]);
    var pretty = new JsonSerializerOptions(AnalysisContract.Json){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    await File.WriteAllTextAsync(Path.Combine(args[0],"monthly-request.json"),JsonSerializer.Serialize(input,pretty));
    await File.WriteAllTextAsync(Path.Combine(args[0],"monthly-response.json"),JsonSerializer.Serialize(output,pretty));
}
Console.WriteLine("PASS: privacy, facts, response validation, guard/quota, HTTP path/auth/retry, malformed/oversized output, no timeout retry, mock contract.");
sealed class FakeHandler(Func<HttpRequestMessage,int,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler {
    public int Calls{get;private set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,++Calls,ct);
}
