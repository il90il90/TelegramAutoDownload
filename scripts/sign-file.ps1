# Signs a single PE file (exe/dll) with Authenticode.
# Requires: Windows SDK (signtool.exe) and env vars CODE_SIGN_PFX + CODE_SIGN_PASSWORD.
# If CODE_SIGN_PFX is not set, exits 0 so unsigned dev builds still work.

param(
    [Parameter(Mandatory, Position = 0)]
    [string]$FilePath
)

if (-not (Test-Path -LiteralPath $FilePath)) {
    Write-Error "File not found: $FilePath"
    exit 1
}

$pfx = $env:CODE_SIGN_PFX
$password = $env:CODE_SIGN_PASSWORD
$timestamp = if ($env:CODE_SIGN_TIMESTAMP) { $env:CODE_SIGN_TIMESTAMP } else { "http://timestamp.digicert.com" }

if (-not $pfx -or -not (Test-Path -LiteralPath $pfx)) {
    Write-Warning "CODE_SIGN_PFX not set — skipping Authenticode sign for: $FilePath"
    exit 0
}

if (-not $password) {
    Write-Error "CODE_SIGN_PASSWORD is required when CODE_SIGN_PFX is set."
    exit 1
}

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    Write-Error "signtool.exe not found. Install the Windows SDK (Signing Tools for Desktop Apps)."
    exit 1
}

Write-Host "Signing: $FilePath"

& $signtool.FullName sign `
    /f $pfx `
    /p $password `
    /tr $timestamp `
    /td sha256 `
    /fd sha256 `
    /d "Telegram Auto Download" `
    /du "https://github.com/il90il90/TelegramAutoDownload" `
    $FilePath

exit $LASTEXITCODE
