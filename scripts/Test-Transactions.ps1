param([string]$BaseUrl, $SessionA, $SessionB, [string]$UserBId)

# Invoked by Test-Accounts in the isolated DB; HTTP helpers are inherited from that script.
$ErrorActionPreference = 'Stop'
try { $koreanZone = [TimeZoneInfo]::FindSystemTimeZoneById('Asia/Seoul') }
catch [TimeZoneNotFoundException] { $koreanZone = [TimeZoneInfo]::FindSystemTimeZoneById('Korea Standard Time') }
$today = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $koreanZone).Date
$date = $today.ToString('yyyy-MM-dd')
$month = $today.ToString('yyyy-MM')
$previousDate = [DateTime]::new($today.Year, $today.Month, 1).AddDays(-1).ToString('yyyy-MM-dd')
$previousMonth = $previousDate.Substring(0,7)
$script:expectedLedgerVersion = 0
function Assert-LedgerMutation($response, [int]$status, [string]$operation) {
    Assert-AccountStatus $response $status $operation
    $script:expectedLedgerVersion++
    if ([long]$response.LedgerVersion -ne $script:expectedLedgerVersion) { throw "$operation produced incorrect ledger version." }
}
function Start-ParallelMutation([string]$method, [string]$path, $body) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method), $BaseUrl + $path)
    $request.Content = [Net.Http.StringContent]::new(($body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
    $request.Headers.Add('X-CSRF-TOKEN', $SessionA.Token)
    return [pscustomobject]@{ Request = $request; Task = $SessionA.Client.SendAsync($request) }
}
function Finish-ParallelMutation($pending) {
    $response = $pending.Task.GetAwaiter().GetResult()
    try { return [pscustomobject]@{ Status = [int]$response.StatusCode; Data = ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json);
        LedgerVersion = (($response.Headers | Where-Object Key -eq 'X-Ledger-Data-Version').Value | Select-Object -First 1) } }
    finally { $response.Dispose(); $pending.Request.Dispose() }
}

$anonymous = New-AccountSession
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/api/transactions') 401 'Anonymous transaction API'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/api/categories') 401 'Anonymous category API'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/Transactions') 302 'Anonymous transaction page'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/api/statistics/monthly') 401 'Anonymous statistics API'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/Dashboard') 302 'Anonymous dashboard'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/api/analysis/reports/latest?month=2026-09') 401 'Anonymous report API'
Assert-AccountStatus (Send-AccountRequest $anonymous GET '/Reports') 302 'Anonymous report page'
Assert-AccountStatus (Send-AccountRequest $SessionA GET '/api/statistics/monthly?month=invalid') 400 'Invalid statistics month'
Assert-AccountStatus (Send-AccountRequest $SessionA GET ('/api/statistics/monthly?month=' + $today.AddMonths(1).ToString('yyyy-MM'))) 400 'Future statistics month'
Assert-AccountStatus (Send-AccountRequest $SessionA GET '/api/categories?type=invalid') 400 'Invalid category type'
Assert-AccountStatus (Send-AccountRequest $SessionA GET '/api/transactions?month=invalid') 400 'Invalid month'
Assert-AccountStatus (Send-AccountRequest $SessionA GET '/api/transactions?page=0') 400 'Invalid pagination'
$payload = @{ type = 'expense'; amount = 12000; categoryId = 1; transactionDate = $date; title = ' Lunch '; memo = ' Test '; userId = $UserBId }
Assert-AccountStatus (Send-AccountRequest $SessionA POST '/api/transactions' $payload $false) 400 'Transaction without CSRF'
$created = Send-AccountRequest $SessionA POST '/api/transactions' $payload
Assert-LedgerMutation $created 201 'Create transaction'
$id = $created.Data.id
if ($created.Data.title -ne 'Lunch' -or $created.Data.memo -ne 'Test' -or $created.Data.version -ne 1) { throw 'Transaction normalization/version failed.' }
Assert-AccountStatus (Send-AccountRequest $SessionB GET "/api/transactions/$id") 404 'Foreign transaction read'
$foreignEdit = @{ type = 'expense'; amount = 15000; categoryId = 1; transactionDate = $date; title = 'Foreign'; version = 1 }
Assert-AccountStatus (Send-AccountRequest $SessionB PUT "/api/transactions/$id" $foreignEdit) 404 'Foreign transaction edit'
Assert-AccountStatus (Send-AccountRequest $SessionB DELETE "/api/transactions/${id}?version=1") 404 'Foreign transaction delete'
Assert-AccountStatus (Send-AccountRequest $SessionB GET "/Transactions/Edit/$id") 404 'Foreign edit form'
Assert-AccountStatus (Send-AccountRequest $SessionB GET "/Transactions/Delete/$id") 404 'Foreign delete form'
foreach ($invalid in @(
    @{ type='expense'; amount=0; categoryId=1; transactionDate=$date; title='Invalid' },
    @{ type='expense'; amount=1.5; categoryId=1; transactionDate=$date; title='Invalid' },
    @{ type='expense'; amount=12000; categoryId=101; transactionDate=$date; title='Invalid' },
    @{ type='expense'; amount=12000; categoryId=1; transactionDate=$today.AddDays(1).ToString('yyyy-MM-dd'); title='Invalid' },
    @{ type='expense'; amount=12000; categoryId=1; title='Invalid' },
    @{ type='expense'; amount=12000; categoryId=1; transactionDate=$date; title='   ' }
)) { Assert-AccountStatus (Send-AccountRequest $SessionA POST '/api/transactions' $invalid) 400 'Invalid transaction input' }

$update = @{ type='expense'; amount=15000; categoryId=4; transactionDate=$date; title='Updated'; version=1 }
Assert-LedgerMutation (Send-AccountRequest $SessionA PUT "/api/transactions/$id" $update) 200 'Update transaction'
Assert-AccountStatus (Send-AccountRequest $SessionA PUT "/api/transactions/$id" $update) 409 'Stale update'
Assert-AccountStatus (Send-AccountRequest $SessionA DELETE "/api/transactions/${id}?version=1") 409 'Stale delete'
Assert-AccountStatus (Send-AccountRequest $SessionA DELETE "/api/transactions/$id") 400 'Missing delete version'
$update.version = 2
$pending1 = Start-ParallelMutation PUT "/api/transactions/$id" $update
$otherUpdate = $update.Clone(); $otherUpdate.title = 'Concurrent update'
$pending2 = Start-ParallelMutation PUT "/api/transactions/$id" $otherUpdate
$parallelUpdates = @((Finish-ParallelMutation $pending1), (Finish-ParallelMutation $pending2))
$updateStatuses = ($parallelUpdates.Status | Sort-Object) -join ','
if ($updateStatuses -ne '200,409') { throw 'Expected one successful concurrent update and one conflict.' }
Assert-LedgerMutation ($parallelUpdates | Where-Object Status -eq 200) 200 'Concurrent update winner'
$fresh = Send-AccountRequest $SessionA GET "/api/transactions/$id"
if ($fresh.Data.version -ne 3) { throw 'Concurrent transaction version increment failed.' }

$income = Send-AccountRequest $SessionA POST '/api/transactions' @{type='income';amount=3500000;categoryId=101;transactionDate=$previousDate;title='Salary'}
Assert-LedgerMutation $income 201 'Previous month income'
$memoTransaction = Send-AccountRequest $SessionA POST '/api/transactions' @{type='expense';amount=5500;categoryId=2;transactionDate=$date;title='<script>alert(1)</script>';memo='<img src=x onerror=alert(1)>'}
Assert-LedgerMutation $memoTransaction 201 'Escaped-content transaction'
$third = Send-AccountRequest $SessionA POST '/api/transactions' @{type='expense';amount=1000;categoryId=3;transactionDate=$date;title='Transport'}
Assert-LedgerMutation $third 201 'Additional expense'
$parallelBody = @{type='expense';amount=2000;categoryId=1;transactionDate=$date;title='Concurrent create'}
$pending1 = Start-ParallelMutation POST '/api/transactions' $parallelBody
$pending2 = Start-ParallelMutation POST '/api/transactions' $parallelBody
$parallelCreates = @((Finish-ParallelMutation $pending1), (Finish-ParallelMutation $pending2)) | Sort-Object { [long]$_.LedgerVersion }
foreach ($response in $parallelCreates) { Assert-LedgerMutation $response 201 'Concurrent create' }
$stats = Send-AccountRequest $SessionA GET "/api/statistics/monthly?month=$month"
Assert-AccountStatus $stats 200 'Statistics with real transactions'
if ($stats.Data.expense -ne 25500 -or $stats.Data.income -ne 0 -or $stats.Data.net -ne -25500 -or $stats.Data.recordCount -ne 5 -or $stats.Data.dataVersion -ne 8) { throw 'Statistics sums/snapshot version failed.' }
if ($stats.Data.dailyAverageExpense -ne [Math]::Round(25500 / [decimal]$today.Day,2,[MidpointRounding]::AwayFromZero)) { throw 'Statistics elapsed-day average failed.' }
if ($stats.Data.largestExpenses.Count -ne 1 -or $stats.Data.largestExpenses[0].amount -ne 15000) { throw 'Largest expense query failed.' }
$statsB = Send-AccountRequest $SessionB GET "/api/statistics/monthly?month=$month&userId=$UserBId"
if ($statsB.Data.recordCount -ne 0 -or $statsB.Data.expense -ne 0 -or $statsB.Data.dataVersion -ne 0) { throw 'Statistics owner isolation failed.' }
$dashboard = Send-AccountRequest $SessionA GET "/Dashboard?month=$month"
Assert-AccountStatus $dashboard 200 'Dashboard with real records'
if ($dashboard.Text.Contains('<script>alert(1)</script>') -or -not $dashboard.Text.Contains('25,500')) { throw 'Dashboard sums/output encoding failed.' }
Assert-AccountStatus (Send-AccountRequest $SessionA POST '/api/analysis/reports' @{month=$month} $false) 400 'Report generation without CSRF'
$reportPending1 = Start-ParallelMutation POST '/api/analysis/reports' @{month=$month}
$reportPending2 = Start-ParallelMutation POST '/api/analysis/reports' @{month=$month}
$reportResponses = @((Finish-ParallelMutation $reportPending1), (Finish-ParallelMutation $reportPending2))
if ((($reportResponses.Status | Sort-Object) -join ',') -ne '201,409') { throw 'Expected one saved report and one in-progress conflict.' }
$savedReport = ($reportResponses | Where-Object Status -eq 201).Data
if ($savedReport.mode -ne 'mock' -or $savedReport.needsRefresh -or $savedReport.dataVersion -ne 8) { throw 'Mock label/report version failed.' }
$serializedInput = $savedReport.input | ConvertTo-Json -Depth 12
if ($serializedInput -match 'userId|email|displayName|title|memo|alert\(1\)') { throw 'Analysis input leaked private fields.' }
$cachedReport = Send-AccountRequest $SessionA POST '/api/analysis/reports' @{month=$month}
Assert-AccountStatus $cachedReport 200 'Cached report'
if ($cachedReport.Data.id -ne $savedReport.id) { throw 'Cache created duplicate report.' }
Assert-AccountStatus (Send-AccountRequest $SessionB GET "/api/analysis/reports/$($savedReport.id)") 404 'Foreign report read'
Assert-AccountStatus (Send-AccountRequest $SessionB GET "/api/analysis/reports/latest?month=$month") 404 'Foreign report latest'
$reportPage = Send-AccountRequest $SessionA GET "/Reports?month=$month"
Assert-AccountStatus $reportPage 200 'Saved report page'
if ($reportPage.Text.Contains('<script>alert(1)</script>')) { throw 'Report output escaping failed.' }
$reportForm = @{Month=$month;__RequestVerificationToken=(Get-FormToken $reportPage.Text)}
Assert-AccountStatus (Send-AccountRequest $SessionA POST '/Reports' $reportForm $false $true) 302 'Cached report form round trip'
$listing = Send-AccountRequest $SessionA GET "/api/transactions?month=$month&pageSize=2"
if ($listing.Data.totalCount -ne 5 -or $listing.Data.items.Count -ne 2 -or $listing.Data.totalPages -ne 3) { throw 'Pagination count failed.' }
$lastPage = Send-AccountRequest $SessionA GET "/api/transactions?month=$month&pageSize=2&page=3"
if ($lastPage.Data.items.Count -ne 1) { throw 'Last page size failed.' }
$pastLastPage = Send-AccountRequest $SessionA GET "/api/transactions?month=$month&pageSize=2&page=999"
if ($pastLastPage.Data.page -ne 3 -or $pastLastPage.Data.items.Count -ne 1) { throw 'Out-of-range page did not resolve to the last existing page.' }
$pastLastFormPage = Send-AccountRequest $SessionA GET "/Transactions?month=$month&pageNumber=999"
Assert-AccountStatus $pastLastFormPage 200 'Out-of-range list page'
if ($pastLastFormPage.Text -notmatch '1 / 1' -or $pastLastFormPage.Text -match '999 /') { throw 'List page displays an invalid current page.' }
$historical = Send-AccountRequest $SessionA GET "/api/transactions?month=$previousMonth&type=income"
if ($historical.Data.totalCount -ne 1 -or $historical.Data.items[0].id -ne $income.Data.id) { throw 'Historical month/type filter failed.' }
$categoryListing = Send-AccountRequest $SessionA GET "/api/transactions?month=$month&categoryId=2"
if ($categoryListing.Data.totalCount -ne 1) { throw 'Category filter failed.' }
$listB = Send-AccountRequest $SessionB GET "/api/transactions?month=$month&userId=$($created.Data.id)"
if ($listB.Data.totalCount -ne 0) { throw 'Owner scope of transaction list failed.' }
$listPage = Send-AccountRequest $SessionA GET "/Transactions?month=$month"
Assert-AccountStatus $listPage 200 'Transaction list page'
if ($listPage.Text.Contains('<script>alert(1)</script>') -or -not $listPage.Text.Contains('&lt;script&gt;')) { throw 'Transaction content escaping failed.' }

foreach ($item in @($fresh.Data, $income.Data, $memoTransaction.Data, $third.Data) + @($parallelCreates.Data)) {
    Assert-LedgerMutation (Send-AccountRequest $SessionA DELETE "/api/transactions/$($item.id)?version=$($item.version)") 204 'Delete own transaction'
}
Assert-AccountStatus (Send-AccountRequest $SessionA GET "/api/transactions/$id") 404 'Deleted transaction'
$staleReport = Send-AccountRequest $SessionA GET "/api/analysis/reports/latest?month=$month"
if (-not $staleReport.Data.needsRefresh -or $staleReport.Data.id -ne $savedReport.id) { throw 'Old successful report did not remain available/stale after mutations.' }
Assert-AccountStatus (Send-AccountRequest $SessionA POST '/api/analysis/reports' @{month=$month}) 400 'Empty month does not generate report'
$preservedReport = Send-AccountRequest $SessionA GET "/api/analysis/reports/latest?month=$month"
if ($preservedReport.Data.id -ne $savedReport.id) { throw 'Failed generation replaced a successful report.' }
Write-Host 'PASS: report generation/cache/forms, duplicate concurrency, CSRF, owner isolation, privacy, stale state, empty-month refusal and prior-result preservation.'

# UI form round trip (same service, antiforgery, hidden version, delete confirmation).
$formPage = Send-AccountRequest $SessionA GET '/Transactions/Create'
Assert-AccountStatus $formPage 200 'Create form'
$formFields = @{ 'Input.Type'='expense'; 'Input.Amount'='5500'; 'Input.CategoryId'='2'; 'Input.TransactionDate'=$date; 'Input.Title'='Form coffee'; 'Input.Version'='1'; '__RequestVerificationToken'=(Get-FormToken $formPage.Text) }
Assert-AccountStatus (Send-AccountRequest $SessionA POST '/Transactions/Create' $formFields $false $true) 302 'Create form post'
$script:expectedLedgerVersion++
$formItem = (Send-AccountRequest $SessionA GET "/api/transactions?month=$month").Data.items[0]
$editPage = Send-AccountRequest $SessionA GET "/Transactions/Edit/$($formItem.id)"
$formFields['Input.Amount']='6000'; $formFields['Input.Title']='Form updated'; $formFields['__RequestVerificationToken']=(Get-FormToken $editPage.Text)
Assert-AccountStatus (Send-AccountRequest $SessionA POST "/Transactions/Edit/$($formItem.id)" $formFields $false $true) 302 'Edit form post'
$script:expectedLedgerVersion++
$deletePage = Send-AccountRequest $SessionA GET "/Transactions/Delete/$($formItem.id)"
$deleteFields = @{ 'Version'='2'; 'ConfirmDeletion'='false'; '__RequestVerificationToken'=(Get-FormToken $deletePage.Text) }
Assert-AccountStatus (Send-AccountRequest $SessionA POST "/Transactions/Delete/$($formItem.id)" $deleteFields $false $true) 200 'Delete without confirmation stays on form'
Assert-AccountStatus (Send-AccountRequest $SessionA GET "/api/transactions/$($formItem.id)") 200 'Unconfirmed delete did not remove transaction'
$deleteFields['ConfirmDeletion']='true'
Assert-AccountStatus (Send-AccountRequest $SessionA POST "/Transactions/Delete/$($formItem.id)" $deleteFields $false $true) 302 'Confirmed delete form'
$script:expectedLedgerVersion++
$empty = Send-AccountRequest $SessionA GET "/api/transactions?month=$month"
if ($empty.Data.totalCount -ne 0 -or $script:expectedLedgerVersion -ne 17) { throw 'Unexpected final transaction count or ledger mutation count.' }
$emptyPastLastPage = Send-AccountRequest $SessionA GET "/api/transactions?month=$month&page=999"
if ($emptyPastLastPage.Data.page -ne 1 -or $emptyPastLastPage.Data.totalPages -ne 1 -or $emptyPastLastPage.Data.items.Count -ne 0) { throw 'Empty list pagination failed.' }
$emptyStats = Send-AccountRequest $SessionA GET "/api/statistics/monthly?month=$month"
if ($emptyStats.Data.recordCount -ne 0 -or $emptyStats.Data.expense -ne 0 -or $emptyStats.Data.dataVersion -ne 17 -or $emptyStats.Data.categories.Count -ne 0) { throw 'Statistics did not reflect deleted transactions.' }
Write-Host 'PASS: statistics API/dashboard, auth/owner isolation, date validation, database aggregates, averages, snapshot versions and mutation refresh.'
Write-Host 'PASS: transaction CRUD/API/forms, owner isolation, invalid inputs, month/category/type filters, pagination, escaping, stale versions, concurrent updates/creates, CSRF and delete confirmation.'
