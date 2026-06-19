# Signs all binaries in the publish folder and optionally the Inno Setup installer.
# Usage:
#   $env:CODE_SIGN_PFX = "C:\path\to\cert.pfx"
#   $env:CODE_SIGN_PASSWORD = "..."
#   .\scripts\sign-release.ps1
#   .\scripts\sign-release.ps1 -SetupExe "TelegramAutoDownload_v2.9.5_Setup.exe"

param(
    [string]$PublishDir = "publish",
    [string]$SetupExe = ""
)

$signFile = Join-Path $PSScriptRoot "sign-file.ps1"
if (-not (Test-Path -LiteralPath $signFile)) {
    Write-Error "sign-file.ps1 not found next to this script."
    exit 1
}

if (-not $env:CODE_SIGN_PFX) {
    Write-Warning "CODE_SIGN_PFX not set — release will remain unsigned (Smart App Control may block it)."
    exit 0
}

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

$binaries = Get-ChildItem -LiteralPath $PublishDir -Recurse -Include *.exe, *.dll -File
foreach ($file in $binaries) {
    & $signFile -FilePath $file.FullName
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($SetupExe -and (Test-Path -LiteralPath $SetupExe)) {
    & $signFile -FilePath $SetupExe
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Signed $($binaries.Count) file(s) in $PublishDir."
