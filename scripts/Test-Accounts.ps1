param([string]$BaseUrl = 'http://127.0.0.1:5078')

# Used only by Test-DatabaseFoundation.ps1 with the isolated test DB.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$clients = [Collections.Generic.List[Net.Http.HttpClient]]::new()
function New-AccountSession {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = [Net.CookieContainer]::new()
    $client = [Net.Http.HttpClient]::new($handler)
    $clients.Add($client)
    return [pscustomobject]@{ Client = $client; Token = $null; Cookies = $handler.CookieContainer }
}
function Send-AccountRequest($session, [string]$method, [string]$path, $body = $null, [bool]$csrf = $true, [bool]$form = $false) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method), $BaseUrl + $path)
    if ($body) {
        if ($form) {
            $fields = [Collections.Generic.Dictionary[string,string]]::new()
            foreach ($key in $body.Keys) { $fields.Add($key, [string]$body[$key]) }
            $request.Content = [Net.Http.FormUrlEncodedContent]::new($fields)
        } else {
            $request.Content = [Net.Http.StringContent]::new(($body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
        }
    }
    if ($csrf -and $session.Token) { $request.Headers.Add('X-CSRF-TOKEN', $session.Token) }
    $response = $session.Client.SendAsync($request).GetAwaiter().GetResult()
    try {
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $data = $null
        if ($text.StartsWith('{')) { $data = $text | ConvertFrom-Json }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Text = $text; Data = $data;
            Location = [string]$response.Headers.Location; SetCookies = ($response.Headers | Where-Object Key -eq 'Set-Cookie').Value;
            LedgerVersion = (($response.Headers | Where-Object Key -eq 'X-Ledger-Data-Version').Value | Select-Object -First 1) }
    } finally { $response.Dispose(); $request.Dispose() }
}
function Refresh-AccountToken($session) {
    $result = Send-AccountRequest $session GET '/api/auth/csrf'
    if ($result.Status -ne 200) { throw 'CSRF token retrieval failed.' }
    $session.Token = $result.Data.requestToken
}
function Assert-AccountStatus($response, [int]$expected, [string]$operation) {
    if ($response.Status -ne $expected) { throw "$operation expected HTTP $expected, got $($response.Status)." }
}
function Get-FormToken([string]$html) {
    $tag = [regex]::Match($html, '<input[^>]*name="__RequestVerificationToken"[^>]*>')
    return [Net.WebUtility]::HtmlDecode([regex]::Match($tag.Value, 'value="([^"]+)"').Groups[1].Value)
}

try {
    $a = New-AccountSession
    $b = New-AccountSession
    $anonymous = New-AccountSession
    $emailA = 'account-a@example.test'
    $emailB = 'account-b@example.test'
    $password = 'MoneyMate9!' + [Guid]::NewGuid().ToString('N')
    Assert-AccountStatus (Send-AccountRequest $anonymous GET '/api/auth/me') 401 'Anonymous API access'
    Assert-AccountStatus (Send-AccountRequest $anonymous GET '/Account/Index') 302 'Anonymous page access'
    Refresh-AccountToken $a
    Refresh-AccountToken $b
    $registration = @{ email = $emailA; displayName = 'Test A'; password = $password; confirmPassword = $password }
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/register' $registration $false) 400 'Registration without CSRF'
    $createdA = Send-AccountRequest $a POST '/api/auth/register' $registration
    Assert-AccountStatus $createdA 201 'Registration A'
    Assert-AccountStatus (Send-AccountRequest $a GET '/api/auth/me') 401 'Registration does not auto-login'
    $registration.email = $emailA.ToUpperInvariant()
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/register' $registration) 409 'Duplicate normalized email'
    $registration.email = 'weak@example.test'; $registration.password = 'weakpass'; $registration.confirmPassword = 'weakpass'
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/register' $registration) 400 'Weak password'
    $registration.email = $emailB; $registration.displayName = '<script>alert(1)</script>'; $registration.password = $password; $registration.confirmPassword = $password
    $createdB = Send-AccountRequest $b POST '/api/auth/register' $registration
    Assert-AccountStatus $createdB 201 'Registration B'
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/login' @{ email = $emailA; password = 'incorrect' } $false) 400 'Login without CSRF'
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/login' @{ email = $emailA; password = $password }) 200 'Login A'
    Assert-AccountStatus (Send-AccountRequest $b POST '/api/auth/login' @{ email = $emailB; password = $password }) 200 'Login B'
    Refresh-AccountToken $a
    Refresh-AccountToken $b
    $meA = Send-AccountRequest $a GET ('/api/auth/me?userId=' + $createdB.Data.id)
    $meB = Send-AccountRequest $b GET '/api/auth/me'
    if ($meA.Data.id -ne $createdA.Data.id -or $meB.Data.id -ne $createdB.Data.id) { throw 'Account session isolation failed.' }
    & (Join-Path $PSScriptRoot 'Test-Transactions.ps1') -BaseUrl $BaseUrl -SessionA $a -SessionB $b -UserBId $createdB.Data.id
    Assert-StatisticsOutage $b
    $profileB = Send-AccountRequest $b GET '/Account/Index'
    Assert-AccountStatus $profileB 200 'Authenticated profile page'
    if ($profileB.Text.Contains('<script>alert(1)</script>') -or -not $profileB.Text.Contains('&lt;script&gt;')) { throw 'Display name HTML escaping failed.' }
    $cookie = $a.Cookies.GetCookies([Uri]$BaseUrl)['MoneyMate.Auth']
    if (-not $cookie -or -not $cookie.HttpOnly -or $cookie.Expires -ne [DateTime]::MinValue) { throw 'Expected HttpOnly session auth cookie.' }
    Assert-AccountStatus (Send-AccountRequest $a GET '/Account/Logout') 302 'GET logout only redirects'
    Assert-AccountStatus (Send-AccountRequest $a GET '/api/auth/me') 200 'GET did not log out'
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/logout' $null $false) 400 'Logout without CSRF'
    Assert-AccountStatus (Send-AccountRequest $a POST '/api/auth/logout') 204 'Logout A'
    Assert-AccountStatus (Send-AccountRequest $a GET '/api/auth/me') 401 'Logged-out API access'
    Assert-AccountStatus (Send-AccountRequest $b GET '/api/auth/me') 200 'Logout A does not log out B'

    # Form registration and login share the service, support antiforgery and reject external return URLs.
    $c = New-AccountSession
    $registerPage = Send-AccountRequest $c GET '/Account/Register'
    $formToken = Get-FormToken $registerPage.Text
    $formFields = @{ 'Input.Email' = 'account-c@example.test'; 'Input.DisplayName' = 'Test C'; 'Input.Password' = $password; 'Input.ConfirmPassword' = $password; '__RequestVerificationToken' = $formToken }
    Assert-AccountStatus (Send-AccountRequest $c POST '/Account/Register' $formFields $false $true) 302 'Form registration'
    $loginPage = Send-AccountRequest $c GET '/Account/Login?ReturnUrl=https%3A%2F%2Fexample.com'
    $loginFields = @{ 'Input.Email' = 'account-c@example.test'; 'Input.Password' = $password; 'ReturnUrl' = 'https://example.com'; '__RequestVerificationToken' = (Get-FormToken $loginPage.Text) }
    $formLogin = Send-AccountRequest $c POST '/Account/Login' $loginFields $false $true
    Assert-AccountStatus $formLogin 302 'Form login'
    if ($formLogin.Location -ne '/Account') { throw 'Unsafe return URL or unexpected form login destination.' }
    Refresh-AccountToken $anonymous
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        Assert-AccountStatus (Send-AccountRequest $anonymous POST '/api/auth/login' @{ email = $emailA; password = 'incorrect' }) 401 'Wrong-password lockout attempt'
    }
    Assert-AccountStatus (Send-AccountRequest $anonymous POST '/api/auth/login' @{ email = $emailA; password = $password }) 401 'Locked account with correct password'
    $limited = $false
    for ($attempt = 0; $attempt -lt 35; $attempt++) {
        $attemptResult = Send-AccountRequest $anonymous POST '/api/auth/login' @{ email = 'missing@example.test'; password = 'incorrect' }
        if ($attemptResult.Status -eq 429) { $limited = $true; break }
        Assert-AccountStatus $attemptResult 401 'Unknown account credentials'
    }
    if (-not $limited) { throw 'Auth request rate limit was not enforced.' }
    Write-Host 'PASS: account API/form registration, duplicate email, password policy, login/logout, identity isolation, CSRF, XSS escaping, safe return URL, session cookie, lockout and rate limit.'
} finally {
    foreach ($client in $clients) { $client.Dispose() }
    $password = $null
}
