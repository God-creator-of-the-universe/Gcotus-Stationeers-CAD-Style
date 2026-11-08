# requires: ASCII or UTF-8 BOM (Windows PS 5.1)
# Compiles GcotuStationeersCADStyle.dll against the game's and BepInEx's DLLs. Where the paths come from: build_paths.ps1.
param([string]$GameDir, [string]$Dotnet, [string]$Csc)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath $PSScriptRoot

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
. (Join-Path $here "build_paths.ps1")
$managed = Join-Path $GameDir "rocketstation_Data\Managed"
$bep = Join-Path $GameDir "BepInEx\core"
$refs = @("mscorlib", "netstandard", "System", "System.Core", "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.AssetBundleModule", "UnityEngine.ImageConversionModule", "UnityEngine.InputLegacyModule", "UnityEngine.IMGUIModule", "UnityEngine.TextRenderingModule") | ForEach-Object { "-r:" + (Join-Path $managed ($_ + ".dll")) }
$refs += "-r:" + (Join-Path $bep "BepInEx.dll")
$refs += "-r:" + (Join-Path $bep "0Harmony.dll")
$out = Join-Path $here "GcotuStationeersCADStyle.dll"
$argList = @($Csc, "-nologo", "-nostdlib", "-noconfig", "-target:library", "-optimize", "-out:$out", (Join-Path $here "GcotuStationeersCADStyle.cs")) + $refs
& $Dotnet @argList
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }
Get-Item $out | Select-Object Name, Length
