<#
.SYNOPSIS
    Builds the installer from an existing portable build.

.DESCRIPTION
    The installer wraps whatever is in artifacts\publish, so the installed and
    portable versions are guaranteed to be the same files. Run
    tools\publish.ps1 first.

.EXAMPLE
    pwsh tools\make-setup.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publish = Join-Path $repo 'artifacts\publish'
$script = Join-Path $repo 'installer\MusicScanIntegrity.iss'

if (-not (Test-Path (Join-Path $publish 'Music_Scan_Integrity.exe'))) {
    throw "Переносимой сборки нет. Сначала выполните: pwsh tools\publish.ps1"
}

if (-not $Version) {
    $props = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
    if ($props -notmatch '<Version>([^<]+)</Version>') {
        throw 'Не удалось прочитать версию из Directory.Build.props.'
    }
    $Version = $Matches[1]
}

# Look for the compiler where Inno Setup usually lives; it may be installed
# anywhere, so the path can also be passed explicitly.
if (-not $Iscc) {
    $candidates = @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
    )

    # Fall back to the install location recorded in the registry.
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like 'Inno Setup*' -and $_.InstallLocation } |
        ForEach-Object { $candidates += (Join-Path $_.InstallLocation 'ISCC.exe') }

    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Iscc -or -not (Test-Path $Iscc)) {
    throw 'ISCC.exe не найден. Установите Inno Setup или укажите путь: -Iscc <путь>'
}

Write-Host "Inno Setup: $Iscc" -ForegroundColor Cyan
Write-Host "Версия: $Version" -ForegroundColor Cyan

& $Iscc "/DAppVersion=$Version" $script
if ($LASTEXITCODE -ne 0) {
    throw 'Установщик не собрался.'
}

$setup = Join-Path $repo "artifacts\Music_Scan_Integrity-$Version-setup.exe"
if (-not (Test-Path $setup)) {
    throw "Установщик не найден там, где ожидался: $setup"
}

$size = (Get-Item $setup).Length / 1MB
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash

Write-Host ''
Write-Host "Готово: $setup" -ForegroundColor Green
Write-Host ("  размер: {0:N1} МБ" -f $size)
Write-Host "  SHA-256: $hash"
