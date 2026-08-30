<#
.SYNOPSIS
    Provision a real seat on this host, assert the whole flow, tear it down, and put the host back.

.DESCRIPTION
    The unit suite cannot reach any of this. 17 of its tests are skipped because they need SudoVDA,
    ViGEmBus, HidHide, an NVIDIA GPU, SYSTEM, or a live seat, so nothing in CI ever exercises an
    end-to-end provision. This is the part a person runs on a real host before a release.

    It provisions one seat, checks what a working seat actually looks like, tears it down, and
    verifies the host came back to where it started.

    SAFETY

      - It REFUSES to run if any seat already exists. A seat on this host is somebody's session,
        and the teardown at the end of this script would take it away.
      - The teardown runs in a finally block, so a failed assertion still removes the seat.
      - It records the console Apollo's PID at the start and checks the same process is still
        there at the end: provisioning a seat must never disturb a console stream.
      - It changes no configuration. Everything it touches, it created.

    WHAT IT CANNOT DO

      It cannot prove a client can stream, because that needs a Moonlight client and a person
      holding it. It proves the seat is ready to be streamed to: the ports listen, the TLS
      handshake completes, the permissions are right, and the seat's Apollo is in the seat's own
      session rather than the console's.

.PARAMETER Account
    The Windows account to provision the seat for. It must already exist.

.PARAMETER TimeoutSeconds
    How long to wait for the seat to reach Ready. Default 90.

.PARAMETER KeepSeat
    Leave the seat up after the checks, for poking at by hand. The host is NOT restored.

.EXAMPLE
    .\smoke-seat.ps1 -Account Gaming
    .\smoke-seat.ps1 -Account Gaming -KeepSeat
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Account,
    [int]$TimeoutSeconds = 90,
    [switch]$KeepSeat
)

$ErrorActionPreference = 'Stop'

# -- result recording ------------------------------------------------
$script:Checks = @()

function Record {
    param([string]$Name, [bool]$Ok, [string]$Actual, [string]$Why = '')
    $script:Checks += [pscustomobject]@{ Name = $Name; Ok = $Ok; Actual = $Actual; Why = $Why }
    $tag = if ($Ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1,-46} {2}" -f $tag, $Name, $Actual) -ForegroundColor $colour
    if (-not $Ok -and $Why) { Write-Host ("         why: {0}" -f $Why) -ForegroundColor DarkGray }
}

function Refuse {
    param([string]$Message)
    Write-Host ''
    Write-Host "REFUSED: $Message" -ForegroundColor Yellow
    Write-Host 'Nothing was provisioned and nothing was changed.' -ForegroundColor Yellow
    exit 2
}

# -- API -------------------------------------------------------------
$keyPath = 'C:\ProgramData\MultiSeat\api-key.txt'
if (-not (Test-Path $keyPath)) { Refuse "no API key at $keyPath - is the service installed?" }
$Headers = @{ 'X-MultiSeat-Key' = (Get-Content $keyPath -Raw).Trim() }
$Api = 'http://127.0.0.1:9550/api'

function Api-Get  { param([string]$Path) Invoke-RestMethod "$Api$Path" -Headers $Headers -TimeoutSec 30 }
function Api-Post { param([string]$Path, $Body)
    if ($null -eq $Body) { return Invoke-RestMethod "$Api$Path" -Method Post -Headers $Headers -TimeoutSec 180 }
    Invoke-RestMethod "$Api$Path" -Method Post -Headers $Headers -TimeoutSec 180 `
        -Body ($Body | ConvertTo-Json) -ContentType 'application/json'
}

function Get-Seats {
    # @(Invoke-RestMethod ...) is NOT safe here, and the reason is worse than it looks.
    #
    # An empty JSON array does not come back as an empty array. Measured on this host:
    #
    #   pwsh 7                 @(...).Count = 1, and element 0 is an EMPTY STRING (not $null)
    #   Windows PowerShell 5.1 @(...).Count = 0
    #
    # So a naive count reports ONE seat on a host with none, on 7 but not on 5.1 - and filtering
    # for $null does not help, because the element is not null. This made the script refuse to run
    # on an empty host while printing a message saying a seat existed, which is exactly the kind of
    # confident wrong answer these checks are supposed to prevent.
    #
    # Filter on what a real seat actually has instead of trusting the shape of the response.
    @(Invoke-RestMethod "$Api/seats" -Headers $Headers -TimeoutSec 30 | Where-Object { $_.id })
}

function Get-SwdNodeCount {
    @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object InstanceId -like 'SWD\MMDEVAPI\*').Count
}

function Get-ConsoleApolloPid {
    $p = Get-Process Sunshine -ErrorAction SilentlyContinue |
         Where-Object { $_.Path -and $_.Path -notlike '*ApolloVibe*' } | Select-Object -First 1
    if ($p) { $p.Id } else { 0 }
}

Write-Host ''
Write-Host '== MultiSeat seat smoke test ==' -ForegroundColor Cyan
Write-Host ''

# -- preconditions ---------------------------------------------------
Write-Host 'Preconditions' -ForegroundColor Cyan

$svc = Get-Service MultiSeatService -ErrorAction SilentlyContinue
if (-not $svc -or $svc.Status -ne 'Running') { Refuse 'MultiSeatService is not running.' }

try { $existing = Get-Seats } catch { Refuse "the API did not answer: $($_.Exception.Message)" }
if ($existing.Count -gt 0) {
    Refuse ("$($existing.Count) seat(s) already exist. This script tears down what it creates, and " +
            'refuses to run where it might take away a seat somebody is using.')
}

try { $null = Get-LocalUser -Name $Account -ErrorAction Stop }
catch { Refuse "no local account named '$Account'. Create it, or pass -Account with one that exists." }

$baselineNodes  = Get-SwdNodeCount
$baselineApollo = Get-ConsoleApolloPid
$consoleSession = (Get-Process -Id $PID).SessionId

Record 'service running'            $true  $svc.Status
Record 'host has no seats'          $true  '0 seats'
Record "account '$Account' exists"  $true  'present'
Write-Host ("  ...   baseline: {0} audio nodes, console session {1}, console Apollo pid {2}" -f `
            $baselineNodes, $consoleSession, $baselineApollo) -ForegroundColor DarkGray
Write-Host ''

# -- provision, check, tear down -------------------------------------
$seat = $null
try {
    Write-Host 'Provisioning' -ForegroundColor Cyan
    $started = Get-Date
    $seat = Api-Post '/seats' @{ accountName = $Account; width = 1920; height = 1080; fps = 60 }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($seat.status -ne 'Ready' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $seat = Get-Seats | Where-Object { $_.id -eq $seat.id }
        if (-not $seat) { break }
    }

    $elapsed = [int]((Get-Date) - $started).TotalSeconds
    Record 'seat reaches Ready' ($seat -and $seat.status -eq 'Ready') `
        ("status={0} in {1}s" -f ($(if ($seat) { $seat.status } else { 'gone' })), $elapsed) `
        'a seat that never reaches Ready makes every check below meaningless'

    if (-not $seat -or $seat.status -ne 'Ready') { throw 'seat did not reach Ready' }

    $sid  = [int]$seat.sessionId
    $base = [int]$seat.portBase

    Write-Host ''
    Write-Host 'The seat itself' -ForegroundColor Cyan

    Record 'seat has its own session' ($sid -gt 0 -and $sid -ne $consoleSession) `
        ("session {0} (console is {1})" -f $sid, $consoleSession) `
        'a seat in the console session injects its input onto the console desktop (issue #18)'

    $apollo = Get-Process -Id $seat.apolloProcessId -ErrorAction SilentlyContinue
    Record 'Apollo is running' ($null -ne $apollo) ("pid {0}" -f $seat.apolloProcessId)

    if ($apollo) {
        Record 'Apollo is in the SEAT session' ($apollo.SessionId -eq $sid) `
            ("Apollo session {0}, seat session {1}" -f $apollo.SessionId, $sid) `
            'this is the guard added in c6141ce; a mismatch is issue #18 reproducing'
        Record 'Apollo is OUR build' ($apollo.Path -like '*ApolloVibe*') $apollo.Path `
            'a seat running the console Apollo would serve the wrong config'
    }

    Write-Host ''
    Write-Host 'Reachability' -ForegroundColor Cyan

    # Parenthesise every element: the comma operator binds TIGHTER than the minus, so
    # @($base - 5, $base, $base + 1) parses as $base - (5, $base, $base + 1) and tries to
    # subtract an ARRAY. It fails with "Object[] does not contain op_Subtraction", which
    # reads like a type problem somewhere else entirely.
    foreach ($port in @(($base - 5), $base, ($base + 1))) {
        $listening = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
        Record ("port $port is listening") ($listening.Count -gt 0) `
            ($(if ($listening.Count) { "pid $($listening[0].OwningProcess)" } else { 'nothing bound' }))
    }

    $webUi = $base + 1
    $tls = $false; $tlsWhy = ''
    try {
        $r = Invoke-WebRequest "https://127.0.0.1:$webUi/" -SkipCertificateCheck -TimeoutSec 15
        $tls = ($r.StatusCode -eq 200)
        $tlsWhy = "HTTP $($r.StatusCode)"
    } catch { $tlsWhy = $_.Exception.Message }
    Record 'TLS handshake on the web UI' $tls $tlsWhy `
        'Apollo must read its own cakey.pem to serve this; a permissions mistake shows up here'

    Write-Host ''
    Write-Host 'Permissions' -ForegroundColor Cyan

    $seatDir = Join-Path 'C:\ProgramData\MultiSeat\apollo' $Account
    $credDir = Join-Path $seatDir 'config\credentials'
    $keyFile = Join-Path $credDir 'cakey.pem'

    if (Test-Path $keyFile) {
        $acl = (icacls $keyFile) -join "`n"
        Record 'TLS key is not readable by every user' ($acl -notmatch 'BUILTIN\\Users') `
            ($(if ($acl -match 'BUILTIN\\Users') { 'BUILTIN\Users present' } else { 'SYSTEM + Admins + seat only' })) `
            'any local user could otherwise read it and impersonate this seat'
        Record 'the seat can write its own TLS key' ($acl -match [regex]::Escape($Account)) `
            ($(if ($acl -match [regex]::Escape($Account)) { "$Account granted" } else { "$Account absent" }))
    } else {
        Record 'TLS key exists' $false 'cakey.pem not found' 'nothing above can be asserted about it'
    }

    foreach ($f in @('sunshine_state.json', 'apps.json')) {
        $path = Join-Path $seatDir "config\$f"
        if (Test-Path $path) {
            $a = (icacls $path) -join "`n"
            Record "$f is writable by the seat" ($a -match [regex]::Escape($Account)) `
                ($(if ($a -match [regex]::Escape($Account)) { "$Account granted" } else { "$Account absent" })) `
                'without this the seat forgets every client it pairs (PR #21)'
        } else {
            Record "$f exists" $false 'not found'
        }
    }

    Write-Host ''
    Write-Host 'Host plumbing' -ForegroundColor Cyan

    $ruleName = "MultiSeat-Seat-$($seat.id -replace '-','')"
    $ruleShown = & netsh advfirewall firewall show rule name="$ruleName-TCP" 2>&1 | Out-String
    Record 'firewall rules were created' ($LASTEXITCODE -eq 0) `
        ($(if ($LASTEXITCODE -eq 0) { $ruleName } else { 'no rule found' })) `
        'the rule name is what teardown deletes by; a mismatch leaks rules forever'

    $mstsc = @(Get-Process mstsc -ErrorAction SilentlyContinue)
    Record 'mstsc keepalive is running' ($mstsc.Count -gt 0) ("{0} process(es)" -f $mstsc.Count) `
        'without it the session goes Disconnected and Apollo display calls start failing'

    Record 'audio did not wedge' ((Get-SwdNodeCount) -ge $baselineNodes) `
        ("{0} nodes (baseline {1})" -f (Get-SwdNodeCount), $baselineNodes) `
        'a collapse to 1 node is the SharedHost audio wedge'
}
finally {
    if ($seat -and -not $KeepSeat) {
        Write-Host ''
        Write-Host 'Teardown' -ForegroundColor Cyan
        try {
            Invoke-RestMethod "$Api/seats/$($seat.id)" -Method Delete -Headers $Headers -TimeoutSec 180 | Out-Null
            Start-Sleep -Seconds 5

            $left = Get-Seats
            Record 'seat is gone' ($left.Count -eq 0) ("{0} seat(s) remain" -f $left.Count)

            Record 'Apollo stopped' ($null -eq (Get-Process -Id $seat.apolloProcessId -ErrorAction SilentlyContinue)) `
                ("pid {0}" -f $seat.apolloProcessId)

            $ruleName = "MultiSeat-Seat-$($seat.id -replace '-','')"
            & netsh advfirewall firewall show rule name="$ruleName-TCP" 2>&1 | Out-Null
            Record 'firewall rules were removed' ($LASTEXITCODE -ne 0) `
                ($(if ($LASTEXITCODE -ne 0) { 'gone' } else { 'STILL PRESENT' })) `
                'rules that outlive their seat accumulate on every provision'

            Record 'audio came back' ((Get-SwdNodeCount) -ge $baselineNodes) `
                ("{0} nodes (baseline {1})" -f (Get-SwdNodeCount), $baselineNodes)

            Record 'console Apollo was never disturbed' ((Get-ConsoleApolloPid) -eq $baselineApollo) `
                ("pid {0} (was {1})" -f (Get-ConsoleApolloPid), $baselineApollo) `
                'provisioning a seat must not touch a console stream'
        }
        catch {
            Record 'teardown completed' $false $_.Exception.Message `
                'THE SEAT MAY STILL BE UP - check the dashboard'
        }
    }
    elseif ($seat -and $KeepSeat) {
        Write-Host ''
        Write-Host ("Seat {0} left running as asked (-KeepSeat). Tear it down from the dashboard." -f $seat.id) -ForegroundColor Yellow
    }
}

# -- verdict ---------------------------------------------------------
$failed = @($script:Checks | Where-Object { -not $_.Ok })

Write-Host ''
Write-Host '== verdict ==' -ForegroundColor Cyan
Write-Host ("  {0} checks, {1} failed" -f $script:Checks.Count, $failed.Count)

if ($failed.Count -gt 0) {
    Write-Host ''
    foreach ($f in $failed) { Write-Host ("  FAILED: {0} -- {1}" -f $f.Name, $f.Actual) -ForegroundColor Red }
    Write-Host ''
    Write-Host 'A seat on this host does not do what a seat is supposed to do.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'A seat provisions, serves, and tears down cleanly on this host.' -ForegroundColor Green
exit 0
