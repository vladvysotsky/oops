# One-command installer build.
# Запускать с Windows-машины из корня репозитория:
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
#
# Делает:
#   1) dotnet publish (Release, win-x64, single-file, self-contained)
#   2) Запускает Inno Setup 6 для упаковки в oops-Setup-<ver>.exe
#
# Требует:
#   - .NET 8 SDK    https://dotnet.microsoft.com/download
#   - Inno Setup 6  https://jrsoftware.org/isinfo.php

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Корень репозитория = родитель папки installer
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$Project  = Join-Path $RepoRoot "Oops\Oops.csproj"
$IssFile  = Join-Path $PSScriptRoot "Oops.iss"
$DistDir  = Join-Path $RepoRoot "dist"

Write-Host "[1/2] dotnet publish..." -ForegroundColor Cyan
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Найти iscc.exe
$Iscc = $null
foreach ($p in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)) {
    if (Test-Path $p) { $Iscc = $p; break }
}
if (-not $Iscc) {
    $Iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue)?.Source
}
if (-not $Iscc) {
    throw "Inno Setup 6 не найден. Установите: https://jrsoftware.org/isinfo.php"
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Host "[2/2] Inno Setup compile (iscc.exe)..." -ForegroundColor Cyan
& $Iscc $IssFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host ""
Write-Host "OK. Installer: $DistDir\oops-Setup-*.exe" -ForegroundColor Green
Get-ChildItem $DistDir -Filter "oops-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
