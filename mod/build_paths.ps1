# requires: ASCII or UTF-8 BOM (Windows PS 5.1)
# Finds what the build needs: the Stationeers game folder ($GameDir), dotnet.exe ($Dotnet) and the Roslyn compiler csc.dll ($Csc).
# Dot-source it from a build script: . (Join-Path $PSScriptRoot "build_paths.ps1")
# Order for each value: what the calling script was given (-GameDir, -Dotnet, -Csc), then build_paths.local.ps1 next to
# this file (your own machine's paths, kept out of git), then the environment variables STATIONEERS_DIR, DOTNET_EXE and
# CSC_DLL, then a search: Steam's library folders for the game, the installed .NET SDKs for the compiler.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath $PSScriptRoot

$callerGameDir = $GameDir; $callerDotnet = $Dotnet; $callerCsc = $Csc
$localPaths = Join-Path $PSScriptRoot "build_paths.local.ps1"
if (Test-Path -LiteralPath $localPaths) { . $localPaths }
if ($callerGameDir) { $GameDir = $callerGameDir }
if ($callerDotnet) { $Dotnet = $callerDotnet }
if ($callerCsc) { $Csc = $callerCsc }

function Find-StationeersDir {
    $steam = $null
    try { $steam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction Stop).SteamPath } catch { }
    if (-not $steam) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path -LiteralPath $vdf) {
        foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) { $libraries += $m.Groups[1].Value.Replace('\\', '\') }
    }
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib "steamapps\common\Stationeers"
        if (Test-Path -LiteralPath (Join-Path $dir "rocketstation.exe")) { return $dir }
    }
    return $null
}

function Find-Dotnet {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $userDotnet) { return $userDotnet }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

# The compiler that ships with the newest installed .NET SDK, next to the dotnet.exe in use.
function Find-Csc([string]$dotnetExe) {
    $roots = @()
    if ($dotnetExe) { $roots += Join-Path (Split-Path -Parent $dotnetExe) "sdk" }
    $roots += Join-Path $env:USERPROFILE ".dotnet\sdk"
    $roots += Join-Path $env:ProgramFiles "dotnet\sdk"
    $best = $null; $bestVersion = $null
    foreach ($root in ($roots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($sdk in Get-ChildItem -LiteralPath $root -Directory) {
            $candidate = Join-Path $sdk.FullName "Roslyn\bincore\csc.dll"
            if (-not (Test-Path -LiteralPath $candidate)) { continue }
            $m = [regex]::Match($sdk.Name, '^\d+(\.\d+){1,3}')
            if (-not $m.Success) { continue }
            $version = [version]$m.Value
            if ($bestVersion -eq $null -or $version -gt $bestVersion) { $best = $candidate; $bestVersion = $version }
        }
    }
    return $best
}

if (-not $GameDir) { $GameDir = $env:STATIONEERS_DIR }
if (-not $GameDir) { $GameDir = Find-StationeersDir }
if (-not $GameDir -or -not (Test-Path -LiteralPath (Join-Path $GameDir "rocketstation.exe"))) {
    throw "Stationeers not found. Pass -GameDir, set STATIONEERS_DIR, or put `$GameDir in build_paths.local.ps1."
}
if (-not $Dotnet) { $Dotnet = $env:DOTNET_EXE }
if (-not $Dotnet) { $Dotnet = Find-Dotnet }
if (-not $Dotnet -or -not (Test-Path -LiteralPath $Dotnet)) { throw "dotnet.exe not found. Install the .NET SDK, pass -Dotnet, or set DOTNET_EXE." }
if (-not $Csc) { $Csc = $env:CSC_DLL }
if (-not $Csc) { $Csc = Find-Csc $Dotnet }
if (-not $Csc -or -not (Test-Path -LiteralPath $Csc)) { throw "csc.dll not found. Install the .NET SDK, pass -Csc, or set CSC_DLL." }
Write-Host ("build paths: game " + $GameDir + " | dotnet " + $Dotnet + " | csc " + $Csc)
