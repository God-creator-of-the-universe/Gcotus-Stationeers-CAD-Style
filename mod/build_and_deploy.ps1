# requires: ASCII or UTF-8 BOM (Windows PS 5.1)
# Builds the shader bundles and the DLL here in mod\, then copies them into the game's local mod folder.
# Never starts the game. -NoDeploy builds only. Where the paths come from: build_paths.ps1.
param([switch]$NoDeploy, [string]$GameDir, [string]$Dotnet, [string]$Csc)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath $PSScriptRoot

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
. (Join-Path $here "build_paths.ps1")
# The child build_plugin.ps1 resolves its paths again; the environment hands it the same ones.
$env:STATIONEERS_DIR = $GameDir; $env:DOTNET_EXE = $Dotnet; $env:CSC_DLL = $Csc
$data = Join-Path $GameDir "rocketstation_Data"
$target = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "My Games\Stationeers\mods\GcotuStationeersCADStyle"

& python -I (Join-Path $here "build_look.py") $data (Join-Path $here "Content")
if ($LASTEXITCODE -ne 0) { throw "build_look.py failed with exit $LASTEXITCODE" }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build_plugin.ps1")
if ($LASTEXITCODE -ne 0) { throw "build_plugin.ps1 failed with exit $LASTEXITCODE" }
if ($NoDeploy) { "built, not deployed"; exit 0 }

if (Get-Process rocketstation -ErrorAction SilentlyContinue) { throw "The game is running; close it before deploying." }
New-Item -ItemType Directory -Force -Path (Join-Path $target "Content"), (Join-Path $target "About") | Out-Null
Copy-Item -LiteralPath (Join-Path $here "GcotuStationeersCADStyle.dll") -Destination $target -Force
Copy-Item -Path (Join-Path $here "Content\*.bundle") -Destination (Join-Path $target "Content") -Force
Copy-Item -LiteralPath (Join-Path $here "Content\bluenoise.png") -Destination (Join-Path $target "Content") -Force
Copy-Item -LiteralPath (Join-Path $here "about.xml") -Destination (Join-Path $target "About\about.xml") -Force
"deployed to $target"
