<#
.SYNOPSIS
    Скачивает 64-битные библиотеки BASS и плагины форматов в native\bass\x64.

.DESCRIPTION
    BASS распространяется un4seen developments (www.un4seen.com) и в репозиторий
    программы не коммитится: у неё своя лицензия (бесплатна для некоммерческого
    использования, для коммерческого нужна лицензия un4seen). Скрипт скачивает
    официальные архивы, достаёт из них только 64-битные DLL и складывает рядом,
    откуда сборка копирует их в подпапку bass\ возле исполняемого файла.

    Запускать один раз перед первой сборкой. Нужен интернет.

.PARAMETER OutputPath
    Куда положить библиотеки. По умолчанию native\bass\x64 в корне репозитория.

.PARAMETER Force
    Перекачать даже то, что уже скачано.

.EXAMPLE
    pwsh tools\fetch-bass.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\native\bass\x64'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Состав: сама библиотека и плагины под форматы из 01_SPECIFICATION.md, раздел 4.
# WAV, AIFF, MP3, OGG, MOD/XM/IT/S3M и MIDI-контейнер BASS понимает сама;
# остальным форматам нужны плагины.
$packages = @(
    @{ Name = 'bass';      Url = 'https://www.un4seen.com/files/bass24.zip';       Dll = 'bass.dll';      Inner = 'x64/bass.dll';         Note = 'ядро: WAV, AIFF, MP3, OGG, MP2' }
    @{ Name = 'bassflac';  Url = 'https://www.un4seen.com/files/bassflac24.zip';   Dll = 'bassflac.dll';  Inner = 'x64/bassflac.dll';     Note = 'FLAC' }
    @{ Name = 'bassalac';  Url = 'https://www.un4seen.com/files/bassalac24.zip';   Dll = 'bassalac.dll';  Inner = 'x64/bassalac.dll';     Note = 'ALAC, M4A' }
    @{ Name = 'bass_aac';  Url = 'https://www.un4seen.com/files/z/2/bass_aac24.zip'; Dll = 'bass_aac.dll'; Inner = 'x64/bass_aac.dll';   Note = 'AAC, M4A, MP4' }
    @{ Name = 'bassape';   Url = 'https://www.un4seen.com/files/bassape24.zip';    Dll = 'bassape.dll';   Inner = 'x64/bassape.dll';      Note = 'APE (Monkey''s Audio)' }
    @{ Name = 'basswv';    Url = 'https://www.un4seen.com/files/basswv24.zip';     Dll = 'basswv.dll';    Inner = 'x64/basswv.dll';       Note = 'WavPack' }
    @{ Name = 'bassopus';  Url = 'https://www.un4seen.com/files/bassopus24.zip';   Dll = 'bassopus.dll';  Inner = 'x64/bassopus.dll';     Note = 'Opus' }
    @{ Name = 'basswma';   Url = 'https://www.un4seen.com/files/basswma24.zip';    Dll = 'basswma.dll';   Inner = 'x64/basswma.dll';      Note = 'WMA' }
    @{ Name = 'bassdsd';   Url = 'https://www.un4seen.com/files/bassdsd24.zip';    Dll = 'bassdsd.dll';   Inner = 'x64/bassdsd.dll';      Note = 'DSD: DSF, DFF' }
    @{ Name = 'bassmidi';  Url = 'https://www.un4seen.com/files/bassmidi24.zip';   Dll = 'bassmidi.dll';  Inner = 'x64/bassmidi.dll';     Note = 'MIDI' }
    # Не входят в обязательный список форматов, но делают рабочим пример
    # «свои расширения» из настроек (.mpc, .tta).
    @{ Name = 'bass_mpc';  Url = 'https://www.un4seen.com/files/z/2/bass_mpc24.zip'; Dll = 'bass_mpc.dll'; Inner = 'x64/bass_mpc.dll';   Note = 'Musepack (.mpc)' }
    @{ Name = 'bass_tta';  Url = 'https://www.un4seen.com/files/z/2/bass_tta24.zip'; Dll = 'bass_tta.dll'; Inner = 'x64/bass_tta.dll';   Note = 'True Audio (.tta)' }
)

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory($OutputPath) | Out-Null

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("bass-fetch-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[System.IO.Directory]::CreateDirectory($temp) | Out-Null

$ok = 0
$failed = @()

try {
    foreach ($package in $packages) {
        $target = Join-Path $OutputPath $package.Dll

        if ((Test-Path $target) -and -not $Force) {
            Write-Host "  уже есть  $($package.Dll)  — $($package.Note)" -ForegroundColor DarkGray
            $ok++
            continue
        }

        $archive = Join-Path $temp "$($package.Name).zip"
        $extract = Join-Path $temp $package.Name

        try {
            Write-Host "  качаем    $($package.Dll)  — $($package.Note)"
            Invoke-WebRequest -Uri $package.Url -OutFile $archive -UseBasicParsing -TimeoutSec 120

            Expand-Archive -Path $archive -DestinationPath $extract -Force

            # В архивах un4seen 64-битная версия лежит в подпапке x64.
            $source = Get-ChildItem -Path $extract -Filter $package.Dll -Recurse |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Select-Object -First 1

            if (-not $source) {
                throw "в архиве не нашлось x64\$($package.Dll)"
            }

            Copy-Item $source.FullName $target -Force
            $ok++
        }
        catch {
            $failed += "$($package.Dll): $($_.Exception.Message)"
            Write-Host "  не вышло  $($package.Dll): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Готово: $ok из $($packages.Count) библиотек в $OutputPath" -ForegroundColor Green

if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host 'Не скачалось:' -ForegroundColor Yellow
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'Эти форматы проверяться не будут. Библиотеки можно скачать вручную' -ForegroundColor Yellow
    Write-Host "с www.un4seen.com и положить 64-битные DLL в $OutputPath" -ForegroundColor Yellow
}

if ($ok -eq 0) {
    exit 1
}
