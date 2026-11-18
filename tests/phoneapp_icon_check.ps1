<#
.SYNOPSIS
  Does S1API report "adaptable" only when a Polyfill fix acted on it? One game run, PASS or FAIL per check.
.DESCRIPTION
  phoneapp-icon-without-file is armed: its patch is in place on every run with S1API, and it only acts when an
  app has no icon file. s1api-portrait-framing is armed too, and it and s1api-custom-customers-save are not
  needed without Polyfill. Before the change every run with S1API reported it adaptable, "repaired by a
  Polyfill fix", and the index told players without Polyfill to install it.

  This runs one mod through Workspace/tools/polyfillrepair/repair.ps1 (profile main, which brings S1API)
  with the given Polyfill, then reads what that run left:

  -Expect armed (the control, any mod without a phone app that lacks an icon file, NPCInteract360 by default):
    1. the fix is armed            Latest.log  "[fix] phoneapp-icon-without-file is armed"
    2. it did not act              Latest.log  no "phoneapp-icon-without-file acted"
    3. not-needed fixes say so     Latest.log  "fix s1api-custom-customers-save applied to ... does not make it adaptable"
    4. S1API is clean              last-run.txt, the M line of S1API, column 7
    5. no repair named for S1API   Latest.log  no "[report] S1API ... repaired by a Polyfill fix"

  -Expect acted (tests/probes/PhoneAppNoIconProbe, an app with neither icon file nor sprite):
    1. the fix is armed
    2. it acted                    Latest.log  "phoneapp-icon-without-file acted in this run"
    3. the app registered          Latest.log  no "[PhoneApp] Failed to register PhoneAppNoIconProbe"
    4. S1API is adaptable          last-run.txt
    5. the log names only that fix Latest.log  "[report] S1API ... repaired by a Polyfill fix (phoneapp-icon-without-file)"
.EXAMPLE
  pwsh -File tests/phoneapp_icon_check.ps1 -Polyfill _wt-Polyfill-armed-fixes -Expect armed
.EXAMPLE
  pwsh -File tests/phoneapp_icon_check.ps1 -Polyfill _wt-Polyfill-armed-fixes -Expect acted
.NOTES
  Build the probe first: dotnet build tests/probes/PhoneAppNoIconProbe/PhoneAppNoIconProbe.csproj -c Debug -p:DeployVerifyDisable=true
  Exit 0 when every check passes, 1 when one fails, 2 when the run itself failed.
#>
[CmdletBinding()]
param(
    [string]$Polyfill = 'main',
    [ValidateSet('armed', 'acted')]
    [string]$Expect = 'acted',
    [string]$ModDll = '',
    [ValidateSet('auto', 'mptest', 'mptest2', 'mptest3', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'auto',
    [int]$WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$root = (Resolve-Path "$repo\..").Path
$repair = Join-Path $root 'Workspace\tools\polyfillrepair\repair.ps1'
$fixId = 'phoneapp-icon-without-file'
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $word = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host "$word  $name - $detail" -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { $script:failed++ }
}

if (-not $ModDll) {
    $ModDll = if ($Expect -eq 'acted') { Join-Path $PSScriptRoot 'probes\PhoneAppNoIconProbe\bin\Debug\net6.0\PhoneAppNoIconProbe.dll' }
              else { Join-Path $root '_backup\slot-stacks-20261020\mptest4\Mods\NPCInteract360_IL2Cpp.dll' }
}
$dll = if (Test-Path -LiteralPath $ModDll) { (Resolve-Path -LiteralPath $ModDll).Path } else { Join-Path $root $ModDll }
if (-not (Test-Path -LiteralPath $dll)) { Write-Host "FAIL  setup - $ModDll does not exist" -ForegroundColor Red; exit 2 }

$runs = Join-Path $root ".s1mcp\repair\$([IO.Path]::GetFileNameWithoutExtension($dll))"
$before = if (Test-Path $runs) { @(Get-ChildItem $runs -Directory | ForEach-Object FullName) } else { @() }

& pwsh -NoProfile -File $repair -ModDll $dll -Polyfill $Polyfill -Slot $Slot -WaitSeconds $WaitSeconds
$code = $LASTEXITCODE
Write-Host "[check] repair.ps1 exited $code"
if ($code -eq 2) { Write-Host 'FAIL  run - repair.ps1 could not finish the run' -ForegroundColor Red; exit 2 }

$run = Get-ChildItem $runs -Directory | Where-Object { $before -notcontains $_.FullName } |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $run) { Write-Host "FAIL  run - no new run folder under $runs" -ForegroundColor Red; exit 2 }
Write-Host "[check] run folder $($run.FullName)"

$log = Join-Path $run.FullName 'Latest.log'
$report = Join-Path $run.FullName 'last-run.txt'
$logText = if (Test-Path $log) { Get-Content $log -Raw } else { '' }

$verdict = ''
if (Test-Path $report) {
    foreach ($line in Get-Content $report) {
        if (-not $line.StartsWith('M|')) { continue }
        $p = $line.Split('|')
        if ($p.Length -gt 6 -and $p[2] -eq 'S1API') { $verdict = $p[6]; break }
    }
}
$repairLine = ([regex]::Match($logText, '\[report\] S1API[^\r\n]*repaired by a Polyfill fix[^\r\n]*')).Value
$acted = $logText -match [regex]::Escape("[fix] $fixId acted in this run")

Check 'fix armed' ($logText -match [regex]::Escape("[fix] $fixId is armed")) "Latest.log says [fix] $fixId is armed"

if ($Expect -eq 'armed') {
    Check 'fix did not act' (-not $acted) "Latest.log has no '$fixId acted'"
    $notNeeded = $logText -match 'fix s1api-custom-customers-save applied to .* does not make it adaptable'
    Check 'not-needed fix said' $notNeeded "Latest.log says s1api-custom-customers-save does not make S1API adaptable"
    Check 'report verdict' ($verdict -eq 'clean') "last-run.txt says '$verdict' for S1API, expected 'clean'"
    Check 'no repair named' (-not $repairLine) $(if ($repairLine) { $repairLine } else { 'Latest.log names no repair for S1API' })
}
else {
    Check 'fix acted' $acted "Latest.log says $fixId acted in this run"
    $failedReg = $logText -match '\[PhoneApp\] Failed to register PhoneAppNoIconProbe'
    Check 'app registered' (-not $failedReg) "Latest.log has no 'Failed to register PhoneAppNoIconProbe'"
    Check 'report verdict' ($verdict -eq 'adaptable') "last-run.txt says '$verdict' for S1API, expected 'adaptable'"
    Check 'log names only that fix' ($repairLine -match "\($([regex]::Escape($fixId))\)") $(if ($repairLine) { $repairLine } else { 'Latest.log names no repair for S1API' })
}

Write-Host "[check] $(if ($failed) { "$failed check(s) failed" } else { 'all checks passed' })"
exit $(if ($failed) { 1 } else { 0 })
