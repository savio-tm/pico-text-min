<#
  Builds Build/TextMin.apk (one TextMeshPro text in PICO Spatial mode). Close the Unity Editor first.
  -Setup    first applies the PICO Spatial project setup (TextMin.Setup) and imports TMP Essential Resources (needed once)
  -Install  then installs and launches it on the connected headset
#>
param([switch]$Setup, [switch]$Install, [string]$Unity = "$env:ProgramFiles\Unity\Hub\Editor\6000.0.59f2\Editor\Unity.exe")
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $project 'Build') | Out-Null
$clock = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Unity([string[]]$What, [string]$Log) {
    $p = Start-Process -FilePath $Unity -PassThru -NoNewWindow -ArgumentList (@('-batchmode', '-quit', '-projectPath', "`"$project`"", '-buildTarget', 'Android', '-logFile', "`"$Log`"") + $What)
    $null = $p.Handle
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "$What failed (exit $($p.ExitCode)). Log: $Log" }
}

if ($Setup) {
    Invoke-Unity @('-executeMethod', 'TextMin.Setup') (Join-Path $project 'Build\setup.log')
    # Window > TextMeshPro > Import TMP Essential Resources (TMP's default font)
    $tmp = Get-ChildItem (Join-Path $project 'Library\PackageCache') -Filter 'TMP Essential Resources.unitypackage' -Recurse | Select-Object -First 1
    Invoke-Unity @('-importPackage', "`"$($tmp.FullName)`"") (Join-Path $project 'Build\tmp-import.log')
}
Invoke-Unity @('-executeMethod', 'TextMin.Apk') (Join-Path $project 'Build\build.log')
Write-Host ("Built in {0:m\:ss}" -f $clock.Elapsed)
if ($Install) {
    # the headset drops off adb while it sleeps during a build: wait for it, and retry once
    adb wait-for-device
    adb install -r (Join-Path $project 'Build\TextMin.apk')
    if ($LASTEXITCODE -ne 0) { adb wait-for-device; adb install -r (Join-Path $project 'Build\TextMin.apk') }
    if ($LASTEXITCODE -ne 0) { throw 'adb install failed (is the headset connected and awake?)' }
    adb shell am force-stop com.vibelab.textmin
    adb shell monkey -p com.vibelab.textmin -c android.intent.category.LAUNCHER 1 | Out-Null
    Write-Host ("Installed and launched in {0:m\:ss}" -f $clock.Elapsed)
}
