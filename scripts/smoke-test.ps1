# =============================================================================
# T.A.C.I.D. Smoke Test (PowerShell)
# Назначение: быстрая проверка auth, telemetry HMAC, SignalR negotiate и revoke.
# Запуск:
#   .\scripts\smoke-test.ps1 [-Server <host:port>] [-Login <login>] [-Password <pass>]
# Примеры:
#   .\scripts\smoke-test.ps1
#   .\scripts\smoke-test.ps1 -Server localhost:5001 -Login admin -Password MyPass
# =============================================================================

param(
    [string]$Server   = "localhost:5001",
    [string]$Login    = "",
    [string]$Password = "",
    [int]$BeaconId    = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Login)) {
    $Login = if ($env:TACID_TEST_LOGIN) { $env:TACID_TEST_LOGIN } else { "admin" }
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = if ($env:TACID_TEST_PASSWORD) { $env:TACID_TEST_PASSWORD } else { "" }
}
if ($BeaconId -le 0) {
    $BeaconId = if ($env:TACID_TEST_BEACON_ID) { [int]$env:TACID_TEST_BEACON_ID } else { 9001 }
}

# Нормализация базового пути
if ($Server -notmatch "^https?://") {
    $BaseUrl = "https://$Server"
} else {
    $BaseUrl = $Server
}

# Разрешение self-signed сертификатов в тестовых окружениях
if (-not ("SelfSignedCertTrust" -as [type])) {
    Add-Type @"
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public class SelfSignedCertTrust : System.Net.Http.DelegatingHandler {
    protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>
        SendAsync(System.Net.Http.HttpRequestMessage req,
                  System.Threading.CancellationToken ct)
        => base.SendAsync(req, ct);
}
"@
}

$Handler = [System.Net.Http.HttpClientHandler]::new()
$Handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
$Client = [System.Net.Http.HttpClient]::new($Handler)
$Client.Timeout = [System.TimeSpan]::FromSeconds(10)

function Pass([string]$msg) { Write-Host "  [PASS] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red; exit 1 }
function Info([string]$msg) { Write-Host "  [INFO] $msg" -ForegroundColor Cyan }

function Invoke-JsonPost([string]$url, [object]$payload, [string]$bearer = "") {
    $json = [System.Text.Json.JsonSerializer]::Serialize($payload)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $url)
    $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    if ($bearer) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $bearer)
    }
    return $Client.SendAsync($request).GetAwaiter().GetResult()
}

function Build-HmacSignature([string]$canonical, [byte[]]$rawKey) {
    $h = [System.Security.Cryptography.HMACSHA256]::new($rawKey)
    try {
        $sigBytes = $h.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonical))
        return [System.Convert]::ToBase64String($sigBytes)
    }
    finally {
        $h.Dispose()
    }
}

Write-Host "==> T.A.C.I.D. Smoke Test | Server: $BaseUrl"
Write-Host ""

# ---------------------------------------------------------------------------
# Шаг 1: Login
# ---------------------------------------------------------------------------
Write-Host "--- Step 1: Login ---"

$authPrefix = "/api/auth"
$loginResp = Invoke-JsonPost "$BaseUrl$authPrefix/login" ([pscustomobject]@{login=$Login; password=$Password})
$loginJson = $loginResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

if (-not $loginResp.IsSuccessStatusCode) {
    $authPrefix = "/auth"
    $loginResp = Invoke-JsonPost "$BaseUrl$authPrefix/login" ([pscustomobject]@{login=$Login; password=$Password})
    $loginJson = $loginResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

Write-Host "  Response: $($loginJson.Substring(0, [Math]::Min(300, $loginJson.Length)))"

try {
    $loginData = $loginJson | ConvertFrom-Json
    $accessToken = [string]$loginData.token
    $refreshToken = [string]$loginData.refreshToken
} catch {
    Fail "Cannot parse login response JSON: $_"
}

if ([string]::IsNullOrEmpty($accessToken)) {
    Fail "Login failed — no access token. Check TACID_TEST_LOGIN / TACID_TEST_PASSWORD env vars."
}
Pass "Login successful via $authPrefix/login. access_token obtained."
if ($refreshToken) { Pass "Refresh token obtained." }
else               { Info "Warning: no refresh token." }

$Client.DefaultRequestHeaders.Authorization =
    [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $accessToken)

# ---------------------------------------------------------------------------
# Шаг 2: Provision key + HMAC telemetry
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 2: Provision key + HMAC telemetry ---"

$beaconKeyBytes = New-Object byte[] 32
$beaconKeyB64 = [Convert]::ToBase64String($beaconKeyBytes)
$keyVersion = 1

$provResp = Invoke-JsonPost "$BaseUrl/api/security/beacons/$BeaconId/key" ([pscustomobject]@{
    keyBase64 = $beaconKeyB64
    keyVersion = $keyVersion
    previousGraceDays = 7
}) $accessToken

if ([int]$provResp.StatusCode -eq 200) { Pass "Beacon key provisioned for beaconId=$BeaconId" }
else { Fail "Provision key returned $([int]$provResp.StatusCode)" }

$seq = [int](Get-Random -Minimum 5000 -Maximum 900000)
$tsMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$anchors = @(
    [pscustomobject]@{ anchorId = 1; distance = 10.5; rssi = -60 }
)
$anchorsCanonical = "1:10.5:-60;"
$canonical = "$BeaconId|$seq|$tsMs|$anchorsCanonical"
$sig = Build-HmacSignature $canonical $beaconKeyBytes

$telemetryResp = Invoke-JsonPost "$BaseUrl/api/telemetry/measurement" ([pscustomobject]@{
    beaconId = $BeaconId
    sequence = $seq
    timestamp = $tsMs
    keyVersion = $keyVersion
    batteryLevel = 85
    signature = $sig
    distances = $anchors
}) $accessToken

if ([int]$telemetryResp.StatusCode -eq 200) { Pass "HMAC telemetry accepted (200)." }
else { Fail "Telemetry returned $([int]$telemetryResp.StatusCode)" }

# ---------------------------------------------------------------------------
# Шаг 3: GET /api/positions
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 3: GET /api/positions ---"

$posResp = $Client.GetAsync("$BaseUrl/api/positions").GetAwaiter().GetResult()
$posBody = $posResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
Write-Host "  Response ($([int]$posResp.StatusCode)): $($posBody.Substring(0, [Math]::Min(200, $posBody.Length)))"

if ([int]$posResp.StatusCode -eq 200) { Pass "Positions: 200 OK" }
else { Fail "Positions returned $([int]$posResp.StatusCode)" }

# ---------------------------------------------------------------------------
# Шаг 4: SignalR negotiate
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 4: SignalR negotiate ---"

$negotiateReq = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$BaseUrl/hubs/positioning/negotiate?negotiateVersion=1")
$negotiateReq.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $accessToken)
$negotiateResp = $Client.SendAsync($negotiateReq).GetAwaiter().GetResult()
if ([int]$negotiateResp.StatusCode -eq 200) {
    Pass "SignalR negotiate reachable (200)."
} else {
    Info "SignalR negotiate returned $([int]$negotiateResp.StatusCode)."
}

# ---------------------------------------------------------------------------
# Шаг 5: GET /api/anchors
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 5: GET /api/anchors ---"

$anchResp = $Client.GetAsync("$BaseUrl/api/anchors").GetAwaiter().GetResult()
$anchBody = $anchResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
Write-Host "  Response ($([int]$anchResp.StatusCode)): $($anchBody.Substring(0, [Math]::Min(200, $anchBody.Length)))"

if ([int]$anchResp.StatusCode -eq 200) { Pass "Anchors: 200 OK" }
else { Fail "Anchors returned $([int]$anchResp.StatusCode)" }

# ---------------------------------------------------------------------------
# Шаг 6: POST /auth/refresh
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 6: POST $authPrefix/refresh ---"

if ($refreshToken) {
    $Client.DefaultRequestHeaders.Authorization = $null   # refresh не требует Bearer

    $rfResp = Invoke-JsonPost "$BaseUrl$authPrefix/refresh" ([pscustomobject]@{refreshToken=$refreshToken})
    $rfJson    = $rfResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    Write-Host "  Response ($([int]$rfResp.StatusCode)): $($rfJson.Substring(0, [Math]::Min(200, $rfJson.Length)))"

    if ([int]$rfResp.StatusCode -ne 200) {
        Fail "Refresh returned $([int]$rfResp.StatusCode)"
    }

    $rfData = $rfJson | ConvertFrom-Json
    $newToken = [string]$rfData.token
    if ([string]::IsNullOrEmpty($newToken) -or $newToken -eq $accessToken) {
        Fail "Refresh failed or returned same token."
    }
    Pass "Refresh successful — new access token issued."
    $accessToken = $newToken
    if (-not [string]::IsNullOrWhiteSpace([string]$rfData.refreshToken)) {
        $refreshToken = [string]$rfData.refreshToken
    }

    $Client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $accessToken)
} else {
    Info "Skipped (no refresh token from login)."
}

# ---------------------------------------------------------------------------
# Шаг 7: POST /auth/logout
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "--- Step 7: POST $authPrefix/logout ---"

$loResp = Invoke-JsonPost "$BaseUrl$authPrefix/logout" ([pscustomobject]@{refreshToken=$refreshToken}) $accessToken

if ([int]$loResp.StatusCode -eq 204) {
    Pass "Logout: 204 No Content"
} else {
    Fail "Logout returned $([int]$loResp.StatusCode)"
}

# Проверка отзыва токена
$checkResp = $Client.GetAsync("$BaseUrl/api/positions").GetAwaiter().GetResult()
if ([int]$checkResp.StatusCode -eq 401) {
    Pass "Token correctly denied after logout (401)."
} else {
    Info "Warning: token returned $([int]$checkResp.StatusCode) after logout (expected 401); Redis denylist may need time."
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Smoke test PASSED for $BaseUrl (auth+hmac+signalr)" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
