# Installs the lab app on Quest headsets, one after another, as they're plugged in over USB.
#
# Usage (PowerShell, from the repo folder):
#   .\tools\install-headsets.ps1 -Apk unity-project\headset-build-room-update.apk
# Then plug headsets in one at a time and wait for a sound before unplugging:
#   Windows "Asterisk" ding = installed, or already has this build: safe to unplug
#   "Hand" error sound      = failed: unplug and plug back in to try again
#   "Exclamation" sound     = put the headset on and allow USB debugging
# Each event is also printed and written to -Log. Stops after -Minutes, or press Ctrl+C.
#
# Notes from 2026-11-19/20 (9 Quest 3S headsets):
# - A headset is skipped if its app was updated after the APK was built, so re-running is safe.
# - Several installs failed or reported "device not found" right as a headset connected; replugging fixed them,
#   and twice the "failed" install had actually finished. So it waits 3 s after a headset connects and retries once.
# - A Quest sleeps when nobody is wearing it, which drops its USB debugging connection. Before installing, the
#   script tells the headset's proximity sensor someone is wearing it (undone afterwards). If installs still
#   fail, wake the headset with its power button before plugging it in.
# - -g grants the app's permissions (the microphone for Hold to talk) at install.
param(
  [Parameter(Mandatory = $true)][string]$Apk,
  [string]$Log = (Join-Path $env:TEMP 'install-headsets.log'),
  [int]$Minutes = 60,
  [string]$Package = 'com.DefaultCompany.VRTemplate'
)

# adb comes with Unity's Android Build Support; use the newest installed editor's copy, else one on PATH.
$adb = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' -ErrorAction SilentlyContinue |
  Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
if (-not $adb) { $adb = (Get-Command adb -ErrorAction SilentlyContinue).Source }
if (-not $adb) { throw "adb not found. Install Android Build Support for Unity, or put adb on PATH." }

$Apk = (Resolve-Path -LiteralPath $Apk).Path
$apkTime = (Get-Item -LiteralPath $Apk).LastWriteTime
function Note($s) { $line = "$(Get-Date -Format 'HH:mm:ss') $s"; Write-Host $line; Add-Content -LiteralPath $Log -Value $line }
function Sound($n) { try { [System.Media.SystemSounds]::$n.Play() } catch {} }
function Install($serial) { (cmd /c "`"$adb`" -s $serial install -r -g `"$Apk`" 2>&1") -join ' ' }

Set-Content -LiteralPath $Log -Value ''
Note "watching for headsets; APK $(Split-Path $Apk -Leaf) built $($apkTime.ToString('yyyy-MM-dd HH:mm'))"
$handled = @{}    # serial -> 'done' | 'failed'
$warned = @{}     # serial -> already asked to allow USB debugging
$firstSeen = @{}  # serial -> when it first showed up ready
$deadline = (Get-Date).AddMinutes($Minutes)

while ((Get-Date) -lt $deadline) {
  $present = @{}
  foreach ($l in (& $adb devices 2>$null | Select-Object -Skip 1 | Where-Object { $_ -match '\S' })) {
    $serial, $state = ($l -split '\s+')[0, 1]
    $present[$serial] = $true
    if ($state -eq 'unauthorized') {
      if (-not $warned[$serial]) { Note "$serial waiting: put the headset on and allow USB debugging"; Sound 'Exclamation'; $warned[$serial] = $true }
      continue
    }
    if ($state -ne 'device' -or $handled[$serial]) { continue }
    if (-not $firstSeen.ContainsKey($serial)) { $firstSeen[$serial] = Get-Date; continue }
    if (((Get-Date) - $firstSeen[$serial]).TotalSeconds -lt 3) { continue }

    $model = (& $adb -s $serial shell getprop ro.product.model 2>$null).Trim()
    $info = & $adb -s $serial shell dumpsys package $Package 2>$null | Select-String 'lastUpdateTime=(.+)' | Select-Object -First 1
    $installedAt = $null
    if ($info) { try { $installedAt = [datetime]::Parse($info.Matches[0].Groups[1].Value.Trim()) } catch {} }
    if ($installedAt -and $installedAt -gt $apkTime) {
      Note "$serial ($model) already has this build (installed $($installedAt.ToString('HH:mm'))). Safe to unplug."
      $handled[$serial] = 'done'; Sound 'Asterisk'; continue
    }

    & $adb -s $serial shell am broadcast -a com.oculus.vrpowermanager.prox_close 2>&1 | Out-Null
    Note "$serial ($model) installing..."
    $out = Install $serial
    if ($out -notmatch 'Success') {
      Note "$serial ($model) first try failed ($($out.Trim() -replace '\s+', ' ')); retrying in 4 s"
      Start-Sleep -Seconds 4
      $out = Install $serial
    }
    & $adb -s $serial shell am broadcast -a com.oculus.vrpowermanager.automation_disable 2>&1 | Out-Null
    if ($out -match 'Success') {
      $after = & $adb -s $serial shell dumpsys package $Package 2>$null | Select-String 'lastUpdateTime=(.+)' | Select-Object -First 1
      Note "$serial ($model) INSTALLED (app updated $($after.Matches[0].Groups[1].Value.Trim())). Safe to unplug."
      $handled[$serial] = 'done'; Sound 'Asterisk'
    } else {
      Note "$serial ($model) FAILED: $($out.Trim() -replace '\s+', ' '). Unplug and plug back in to retry."
      $handled[$serial] = 'failed'; Sound 'Hand'
    }
  }
  # A failed headset is retried when it's unplugged and plugged back in.
  foreach ($s in @($handled.Keys)) { if (-not $present[$s] -and $handled[$s] -eq 'failed') { $handled.Remove($s) } }
  foreach ($s in @($warned.Keys)) { if (-not $present[$s]) { $warned.Remove($s) } }
  foreach ($s in @($firstSeen.Keys)) { if (-not $present[$s]) { $firstSeen.Remove($s) } }
  Start-Sleep -Seconds 2
}
Note "stopped after $Minutes minutes; $(@($handled.Values | Where-Object { $_ -eq 'done' }).Count) headset(s) have this build"
