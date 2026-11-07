# Checks that a customer added through S1API is written to the save and stays unlocked after a reload with
# Polyfill installed (fix s1api-custom-customers-save). Personnel adds the customer from a small pack this
# script writes (Una Probe, locked by its connection to an unlocked town customer); a town customer put back to
# locked is the control. Both are unlocked the way an accepted free sample unlocks them, the game saves, goes
# back to the menu and loads the same save again.
#
#     pwsh -File tests/s1api_customer_save_check.ps1 -Slot mptest            # builds this checkout, starts the slot
#     pwsh -File tests/s1api_customer_save_check.ps1 -NoStart -Slot mptest   # game already running with the pack
#
# Uses Personnel's debug console commands (`personnel rel <id> direct`, `personnel cust <id> dump`), so Personnel
# must be a Debug build. Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [switch]$NoStart,
    [int]$Save = 5,
    # A town customer unlocked in the 'played' template; the probe customer is connected to it.
    [string]$Connection = 'kyle_cooley',
    # The town customer used as the control; it is locked first, then unlocked like the probe.
    [string]$Vanilla = 'austin_steiner',
    [string]$ProbeId = 'unlockprobe_una_probe'
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$una = $ProbeId
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function ReadLog() { return ReadLogFrom 0 }

# Personnel's probe lines are read from the game log; the bridge's run output carries unrelated warnings.
function Probe([string]$what, [string]$npc, [string]$action) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel $what $npc $action *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for '$what $npc $action'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match "\[Personnel\] (cust|rel) " })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

function Bridge([string]$command, [string]$argsJson = '{}', [int]$timeoutMs = 12000) {
    $out = (& python -I $call --slot $Slot $command $argsJson --timeout $timeoutMs 2>&1 | Out-String).Trim()
    Write-Host "  bridge $command -> $out"
    return ($LASTEXITCODE -eq 0)
}

function WaitReady([int]$timeout = 900) {
    & python -I $s1 --slot $Slot wait-ready --timeout $timeout | Out-Host
    return ($LASTEXITCODE -eq 0)
}

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Polyfill')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$steamid = [string]$table.$Slot.steamid
$script:log = Join-Path $gameRoot 'MelonLoader\Latest.log'
$npcsJson = Join-Path $env:USERPROFILE "AppData\LocalLow\TVGS\Schedule I\Saves\$steamid\SaveGame_$Save\NPCs.json"
Write-Host "slot $Slot -> $gameRoot"

if (-not $NoStart) {
    # A customer as the Personify editor exports it: locked by its connection, a routine in Northtown.
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\UnlockProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Unlock Probe", "packId": "unlockprobe", "autoRegister": true,
  "npcs": [
    { "name": "Una Probe", "appearance": { "height": 1.0 },
      "customer": { "spending": { "min": 200, "max": 500 }, "ordersPerWeek": { "min": 1, "max": 5 }, "standards": "Moderate", "allowDirectApproach": true },
      "relationships": { "delta": 1, "unlockType": "Recommendation", "connections": [ "$Connection" ] },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Build', (Split-Path $repo -Leaf), '-Mods', 'Personnel,Polyfill', '-NoApp', '-Save', "$Save")
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

$boot = ReadLog
Report 'Polyfill keeps game patches native-only' ($boot -match 'managed shortcut is left out') 'the native-only layer did not install, so this run does not test the fix'
Report 'fix s1api-custom-customers-save applied' ($boot -match '\[fix\] s1api-custom-customers-save: customers added by mods') 'no applied line for the fix in the log'

# 1. Start state: the probe is locked (it has a connection); the town control is put back to locked.
$d = Probe 'cust' $una 'dump'
Report 'probe customer spawned' ($d -match "cust dump $una") "no live NPC $una"
Report 'probe customer starts locked' ($d -match 'relUnlocked=False') 'relUnlocked is not False'
Probe 'cust' $Vanilla 'lock' | Out-Null

# 2. Unlock both the way an accepted sample does.
foreach ($npc in @($una, $Vanilla)) {
    $u = Probe 'rel' $npc 'direct'
    Report "$npc unlocked before the save" ($u -match 'unlocked=True') ([regex]::Match($u, 'unlocked=\S+').Value)
}

# 3. Save; the probe must be in NPCs.json. Then back to the menu and load the same save.
$beforeSave = (Get-Item -LiteralPath $script:log).Length
Report 'save' (Bridge 'save_now') 'save_now failed'
Start-Sleep -Seconds 5
$saved = if (Test-Path -LiteralPath $npcsJson) { [IO.File]::ReadAllText($npcsJson) } else { '' }
Report "$una written to NPCs.json" ($saved -match [regex]::Escape("\`"$una\`"")) "not in $npcsJson"
Report "$Vanilla written to NPCs.json" ($saved -match [regex]::Escape("\`"$Vanilla\`"")) "not in $npcsJson"
$saveLog = ReadLogFrom $beforeSave
$fixLine = @($saveLog -split "`r?`n" | Where-Object { $_ -match "s1api-custom-customers-save: '$una'" })
foreach ($l in $fixLine) { Write-Host "  $($l.Trim())" }
Report 'error-free save' (-not ($saveLog -match 's1api-custom-customers-save: could not')) 'the fix logged an error during the save'

Report 'return to menu' (Bridge 'return_to_menu') 'return_to_menu failed'
Start-Sleep -Seconds 10
$loaded = $false
for ($i = 0; $i -lt 12 -and -not $loaded; $i++) {
    $loaded = Bridge 'load_save' "{`"slot`": $Save}" 90000
    if (-not $loaded) { Start-Sleep -Seconds 5 }
}
Report 'load the save again' $loaded 'load_save failed'
Report 'game ready after the load' (WaitReady) 'wait-ready failed'

# 4. Both are still unlocked.
foreach ($npc in @($una, $Vanilla)) {
    $d = Probe 'cust' $npc 'dump'
    Report "$npc unlocked after the load" ($d -match 'relUnlocked=True') ([regex]::Match($d, 'relUnlocked=\S+').Value)
    Report "$npc listed as unlocked customer after the load" ($d -match 'inUnlocked=True') ([regex]::Match($d, 'inUnlocked=\S+').Value)
}

$dll = Join-Path $gameRoot 'Mods\Polyfill.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Polyfill.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))
