# Fetches the pinned PDFium build the ingest host links

$ErrorActionPreference = "Stop"

$build = "8057"
$archive = "pdfium-win-x64.tgz"
$url = "https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F$build/$archive"
$sha256 = "E307D519E42F2E69B1B531F0C2A32DFFCDF3891EC0EBA60328BA51A57CEC01ED"

$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root "external\pdfium"
$pin = Join-Path $dest ".pin"

if ((Test-Path $pin) -and ((Get-Content $pin) -eq $sha256)) {
    Write-Host "PDFium $build already installed"
    exit 0
}

$tgz = Join-Path ([IO.Path]::GetTempPath()) $archive
Write-Host "Downloading $archive"
curl.exe -4 --retry 3 --fail --location -o $tgz $url
if ($LASTEXITCODE -ne 0) { throw "download failed: $url" }

$actual = (Get-FileHash $tgz -Algorithm SHA256).Hash
if ($actual -ne $sha256) { throw "SHA256 mismatch: expected $sha256, got $actual" }

Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dest | Out-Null
tar.exe -xzf $tgz -C $dest
if ($LASTEXITCODE -ne 0) { throw "extract failed: $tgz" }

Set-Content $pin $sha256
Remove-Item $tgz -Force -ErrorAction SilentlyContinue
Write-Host "PDFium $build installed at $dest"
