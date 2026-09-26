<#
.SYNOPSIS
  Builds PartyTime, copies it into the game's Mods folder and packs release\PartyTime-<version>.zip

.PARAMETER Configuration
  Release (default) or Debug.

.PARAMETER GameDir
  DragNWash.exe folder.
#>
param(
    [string]$Configuration = "Release",
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$name = "PartyTime"
$version = (Get-Content (Join-Path $root "mod.json") -Raw | ConvertFrom-Json).version

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path ([System.IO.Path]::Combine($dir, "DragNWash.exe")))
}

function Get-SteamLibraries {
    $libraries = @()
    try { $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath } catch { $steam = $null }
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
        }
    }
    $libraries += "C:\Program Files (x86)\Steam"
    return $libraries | Select-Object -Unique
}

function Find-GameDir {
    if (Test-GameDir $env:DNW_GAME_DIR) { return $env:DNW_GAME_DIR }
    foreach ($library in Get-SteamLibraries) {
        $candidate = Join-Path $library "steamapps\common\Drag'n Wash"
        if (Test-GameDir $candidate) { return $candidate }
    }
    return $null
}

if ($GameDir -eq "") { $GameDir = Find-GameDir }
if (-not (Test-GameDir $GameDir)) {
    throw "Drag'n Wash was not found. Pass -GameDir <game folder> or set the DNW_GAME_DIR environment variable."
}
$GameDir = (Resolve-Path $GameDir).Path.TrimEnd('\')
if (-not (Test-Path (Join-Path $GameDir "DnWModLoader\DnWModLoader.dll"))) {
    throw "The DnW Mod Loader is not installed in $GameDir; install it first."
}

Write-Host "Building $name $version ($Configuration) against $GameDir ..." -ForegroundColor Cyan
& dotnet build (Join-Path $root "$name.csproj") -c $Configuration -nologo "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

Write-Host "Packing the release..." -ForegroundColor Cyan
$bin = Join-Path $root "bin\$Configuration"
$release = Join-Path $root "release"
$stage = Join-Path $release "stage"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage $name) | Out-Null
foreach ($f in @("$name.dll", "$name.pdb", "mod.json")) { Copy-Item (Join-Path $bin $f) (Join-Path $stage $name) }

$zip = Join-Path $release "$name-$version.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Done: $zip" -ForegroundColor Green
