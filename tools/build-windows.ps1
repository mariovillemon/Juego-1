# Builds the Windows x64 player without opening the editor UI.
# Usage: powershell -ExecutionPolicy Bypass -File tools/build-windows.ps1 [-Unity "C:\...\Unity.exe"] [-Development] [-Out dir]
# Output (default): Builds\Windows\Taller.exe. Log: Builds\build-windows.log
param(
    [string]$Unity = $env:UNITY,
    [switch]$Development,
    [string]$Out = ""
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Unity) {
    $cand = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\6000.0.*\Editor\Unity.exe" -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
    if ($cand) { $Unity = $cand.FullName }
}
if (-not $Unity -or -not (Test-Path $Unity)) {
    Write-Error "No encuentro Unity 6000.0: pasa -Unity con la ruta de Unity.exe o define la variable UNITY."
    exit 2
}
if (-not $Out) { $Out = Join-Path $Root "Builds\Windows" }
& (Join-Path $PSScriptRoot "sync-sim-to-unity.ps1")
New-Item -ItemType Directory -Force -Path (Join-Path $Root "Builds") | Out-Null
$log = Join-Path $Root "Builds\build-windows.log"
$unityArgs = @("-batchmode", "-quit", "-projectPath", (Join-Path $Root "Unity"), "-buildTarget", "Win64",
          "-executeMethod", "Garage.Unity.EditorTools.BuildWindows.CommandLine", "-buildPath", $Out, "-logFile", $log)
if ($Development) { $unityArgs += "-development" }
$p = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) {
    Write-Error "La build ha fallado (código $($p.ExitCode)). Revisa $log"
    exit $p.ExitCode
}
Write-Host "Build lista: $(Join-Path $Out 'Taller.exe')"
