# Checks fix mea-wrapped-routes and AutoRestock in a game that is already running with
# MoreEmployeeAssignments, Harder Working Employees (Packager MaxRoutes above 5) and AutoRestock loaded, and
# a save with at least one Packager.
#
#     pwsh -File tests/mea_wrapped_routes_check.ps1 -Slot mptest6            # fix on: no error, fix line
#     pwsh -File tests/mea_wrapped_routes_check.ps1 -Slot mptest6 -FixOff    # with DisabledFixes = "mea-wrapped-routes"
#
# Opens every Packager's management panel with the debug command `polyfillpanel packager <n>` (Polyfill must
# be a Debug build), takes a screenshot of each, and calls AutoRestock's day pass, which sends Oscar's
# receipt through the bridged MessagingManager.SendMessage. Prints PASS or FAIL per check and exits 1 when
# any check fails. The game keeps running.
param(
    [Parameter(Mandatory)] [string]$Slot,
    # The fix is switched off in this run: the MoreEmployeeAssignments error is then the expected result.
    [switch]$FixOff
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0
$meaError = 'Could not create the Packager scroll views'

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Run([string]$command) {
    $out = & python $s1 --slot $Slot run $command 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { Write-Host "s1.py run '$command' exit $LASTEXITCODE"; Write-Host $out }
    return $out
}

$first = Run 'polyfillpanel packager 1'
if ($first -notmatch 'of (\d+), ') {
    Report 'a Packager panel opens' $false ("polyfillpanel gave no 'opened' line: " + $first.Trim())
    exit 1
}
$count = [int]$Matches[1]
Report 'a Packager panel opens' $true ''
$outputs = @($first)
$shots = @()
for ($n = 1; $n -le $count; $n++) {
    if ($n -gt 1) { $outputs += Run "polyfillpanel packager $n" }
    $shot = (& python $s1 --slot $Slot shot 2>&1 | Out-String).Trim()
    $shots += $shot
    Write-Host "packager $n of ${count}: $shot"
}
$null = Run 'polyfillpanel close'
$all = $outputs -join "`n"

if ($FixOff) {
    Report 'without the fix, MoreEmployeeAssignments fails to build its scroll view' ($all -match [regex]::Escape($meaError)) 'no MoreEmployeeAssignments error; the setup does not reproduce'
} else {
    Report 'no MoreEmployeeAssignments scroll view error' ($all -notmatch [regex]::Escape($meaError)) 'the error is still logged'
    $fixLine = & python $s1 --slot $Slot log --grep '[fix] mea-wrapped-routes: the Packager' -n 5 2>&1 | Out-String
    Report 'the fix left the scrolling to the other mod' ($fixLine -match 'already scroll in') ('no fix line in the log: ' + $fixLine.Trim())
}

$day = Run 'polyfillprobe AutoRestock.Manager::OnDayPass'
Report "AutoRestock's day pass sends Oscar's receipt" (($day -match 'CALLED as a static') -and ($day -notmatch 'Exception')) $day.Trim()

if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'
