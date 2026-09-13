<#
.SYNOPSIS
    Собирает переносимую версию программы в artifacts\publish.

.DESCRIPTION
    Публикует self-contained сборку под win-x64: .NET на компьютере пользователя
    не нужен, всё лежит внутри папки. Установщика нет — папку достаточно скопировать.
    Библиотеки BASS должны быть скачаны заранее (tools\fetch-bass.ps1).

.EXAMPLE
    pwsh tools\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\publish'),
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'src\MusicScanIntegrity.App\MusicScanIntegrity.App.csproj'
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$bassSource = Join-Path $repo 'native\bass\x64'

if (-not (Test-Path $bassSource)) {
    Write-Host 'Библиотеки BASS не найдены. Сначала выполните:' -ForegroundColor Yellow
    Write-Host '  pwsh tools\fetch-bass.ps1' -ForegroundColor Yellow
    Write-Host 'Без них программа соберётся, но проверять файлы не сможет.' -ForegroundColor Yellow
    Write-Host ''
}

if (-not $SkipTests) {
    Write-Host 'Тесты…' -ForegroundColor Cyan
    & dotnet test (Join-Path $repo 'MusicScanIntegrity.sln') -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw 'Тесты не прошли — сборка остановлена.'
    }
}

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}

Write-Host 'Публикация…' -ForegroundColor Cyan
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $OutputPath `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw 'Публикация не удалась.'
}

# Отладочные символы в переносимой сборке ни к чему.
Get-ChildItem $OutputPath -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

$size = (Get-ChildItem $OutputPath -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
$bassCount = if (Test-Path (Join-Path $OutputPath 'bass')) {
    (Get-ChildItem (Join-Path $OutputPath 'bass') -Filter *.dll).Count
} else { 0 }

Write-Host ''
Write-Host "Готово: $OutputPath" -ForegroundColor Green
Write-Host ("  размер: {0:N0} МБ" -f $size)
Write-Host "  библиотек BASS: $bassCount"
Write-Host ''
Write-Host 'Папку можно скопировать целиком — .NET у получателя не нужен.'
Write-Host 'Сборка без цифровой подписи: при первом запуске Windows покажет'
Write-Host 'предупреждение «неизвестный издатель» — это ожидаемо.'
