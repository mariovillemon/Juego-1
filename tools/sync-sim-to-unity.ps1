# Copies the simulation sources and the game data into the Unity project (idempotent).
$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Dest = Join-Path $Root "Unity/Assets/Garage/Sim/Generated"
if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force }
foreach ($p in @("Garage.Sim", "Garage.Data", "Garage.Game")) {
  $src = Join-Path $Root "src/$p"
  Get-ChildItem $src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $src).Path.Length + 1)
    $target = Join-Path (Join-Path $Dest $p) $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item $_.FullName $target
  }
}
$Data = Join-Path $Root "Unity/Assets/StreamingAssets/data"
if (Test-Path $Data) { Remove-Item $Data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Data | Out-Null
Copy-Item (Join-Path $Root "data/base") $Data -Recurse
Copy-Item (Join-Path $Root "data/schemas") $Data -Recurse
Write-Host "Sincronizado en $Dest y $Data"
