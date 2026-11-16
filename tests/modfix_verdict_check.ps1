<#
.SYNOPSIS
  Does a mod that only a ModFix repairs report "adaptable"? One game run, PASS or FAIL per check.
.DESCRIPTION
  A ModFix patches a mod's own code and leaves no triage finding, so the verdict used to be computed from
  findings alone and came out "clean". The index prints "clean" plus clean play as "Works", which reads as
  "runs without Polyfill".

  This runs NPCInteract360 1.1.1 (fix npcinteract360-talk-angle, no findings on 0.4.7) through
  Workspace/tools/polyfillrepair/repair.ps1 with the given Polyfill, then reads what that run left:
    1. the fix applied              Latest.log  "[fix] npcinteract360-talk-angle:"
    2. the verdict in the report    last-run.txt, the M line of NPCInteract360, column 7
    3. the verdict said in the log  Latest.log  "[report] ... repaired by a Polyfill fix" (only for adaptable)

  -Expect clean is the run against a Polyfill from before the change, -Expect adaptable the one after it.
.EXAMPLE
  pwsh -File tests/modfix_verdict_check.ps1 -Polyfill main -Expect clean
.EXAMPLE
  pwsh -File tests/modfix_verdict_check.ps1 -Polyfill _wt-Polyfill-modfixlabel -Expect adaptable
.NOTES
  Exit 0 when every check passes, 1 when one fails, 2 when the run itself failed.
#>
[CmdletBinding()]
param(
    [string]$Polyfill = 'main',
    [ValidateSet('clean', 'adaptable')]
    [string]$Expect = 'adaptable',
    [string]$ModDll = '_backup/slot-stacks-20261020/mptest4/Mods/NPCInteract360_IL2Cpp.dll',
    [string]$ModName = 'NPCInteract360',
    [string]$FixId = 'npcinteract360-talk-angle',
    [ValidateSet('auto', 'mptest', 'mptest2', 'mptest4', 'mptest5', 'mptest6')]
    [string]$Slot = 'auto',
    [int]$WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$repair = Join-Path $root 'Workspace\tools\polyfillrepair\repair.ps1'
$failed = 0

function Check([string]$name, [bool]$ok, [string]$detail) {
    $word = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host "$word  $name - $detail" -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { $script:failed++ }
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

Check 'fix applied' ($logText -match [regex]::Escape("[fix] ${FixId}:")) "Latest.log names [fix] $FixId"

$verdict = ''
if (Test-Path $report) {
    foreach ($line in Get-Content $report) {
        if (-not $line.StartsWith('M|')) { continue }
        $p = $line.Split('|')
        if ($p.Length -gt 6 -and ($p[3] -eq $ModName -or $p[2] -like "$ModName*")) { $verdict = $p[6]; break }
    }
}
Check 'report verdict' ($verdict -eq $Expect) "last-run.txt says '$verdict' for $ModName, expected '$Expect'"

if ($Expect -eq 'adaptable') {
    $said = $logText -match "\[report\] .*$([regex]::Escape($ModName)).*repaired by a Polyfill fix"
    Check 'log names the fix' $said "Latest.log says $ModName is repaired by a Polyfill fix"
}

Write-Host "[check] $(if ($failed) { "$failed check(s) failed" } else { 'all checks passed' })"
exit $(if ($failed) { 1 } else { 0 })
