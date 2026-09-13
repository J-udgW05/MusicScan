<#
.SYNOPSIS
    Снимает окно программы в PNG — для проверки интерфейса при разработке.

.DESCRIPTION
    Запускает собранное приложение (или подключается к уже запущенному),
    ждёт появления главного окна и сохраняет его снимок через PrintWindow.
    Скрипт вспомогательный: в работе программы не участвует.

.EXAMPLE
    pwsh tools\capture-window.ps1 -Output shot.png
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\MusicScanIntegrity.App\bin\Debug\net10.0-windows\win-x64\Music_Scan_Integrity.exe'),
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\screenshot.png'),
    [int]$WaitSeconds = 12,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$process = $null
$started = $false

$existing = Get-Process -Name 'Music_Scan_Integrity' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existing) {
    $process = $existing
}
else {
    if (-not (Test-Path $Exe)) { throw "Не найден $Exe — сначала соберите проект." }
    $process = Start-Process -FilePath $Exe -PassThru
    $started = $true
}

$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) { throw "Программа завершилась с кодом $($process.ExitCode)." }
    if ($process.MainWindowHandle -ne [IntPtr]::Zero -and [Win]::IsWindowVisible($process.MainWindowHandle)) { break }
    Start-Sleep -Milliseconds 400
}

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw 'Главное окно не появилось.' }

[Win]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 1200

$rect = New-Object Win+RECT
[Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw 'Не удалось определить размеры окна.' }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
# Флаг 2 = PW_RENDERFULLCONTENT: без него окна с аппаратным ускорением выходят чёрными.
[Win]::PrintWindow($handle, $hdc, 2) | Out-Null
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

$Output = [System.IO.Path]::GetFullPath($Output)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Output)) | Out-Null
$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host "Снимок: $Output ($width x $height)"

if ($started -and -not $KeepRunning) {
    $process.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 800
    if (-not $process.HasExited) { $process.Kill() }
}
