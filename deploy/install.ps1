#Requires -Version 5.1
<#
.SYNOPSIS
    Устанавливает Wellbeing: файлы в Program Files, служба Windows с автоперезапуском,
    права на папки, автозапуск агента в трее, ярлык в меню «Пуск».
    Повторный запуск обновляет уже установленную версию (данные сохраняются).
.PARAMETER Source
    Папка с собранными файлами (по умолчанию — папка скрипта или ..\dist).
.PARAMETER Port
    Порт API на 127.0.0.1 (по умолчанию 47613).
#>
param(
    [string]$Source,
    [int]$Port = 47613
)

$ErrorActionPreference = 'Stop'

# Без прав администратора — перезапускаемся с повышением (UAC) в отдельном окне.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Нужны права администратора — запрашиваю повышение (UAC)…'
    $arguments = "-NoProfile -NoExit -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Port $Port"
    if ($Source) { $arguments += " -Source `"$((Resolve-Path $Source).Path)`"" }
    try { Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments }
    catch { Write-Warning 'Установка отменена: права администратора не получены.' }
    exit
}

$ServiceName = 'WellbeingSvc'
$InstallDir = Join-Path $env:ProgramFiles 'Wellbeing'
$DataDir = Join-Path $env:ProgramData 'Wellbeing'
$RunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$Shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Wellbeing.lnk'

# SID-ы вместо имён групп — чтобы работало на Windows с любым языком.
$System = '*S-1-5-18'
$Admins = '*S-1-5-32-544'
$Users = '*S-1-5-32-545'

function Step([string]$text) { Write-Host "-> $text" -ForegroundColor Cyan }

if (-not $Source) {
    $Source = if (Test-Path (Join-Path $PSScriptRoot 'Wellbeing.Service.exe')) { $PSScriptRoot } else { Join-Path $PSScriptRoot '..\dist' }
}
$Source = (Resolve-Path $Source).Path
foreach ($file in 'Wellbeing.Service.exe', 'Wellbeing.Agent.exe') {
    if (-not (Test-Path (Join-Path $Source $file))) { throw "Не найден $file в $Source. Сначала соберите проект: deploy\build.ps1" }
}

$existing = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Step 'Останавливаю установленную версию'
    if ($existing.Status -ne 'Stopped') { Stop-Service $ServiceName -Force }
    Get-Process Wellbeing.Agent -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Явные права ставим только на саму папку — флаги наследования (OI)(CI) имеют смысл только для папок.
# Всё содержимое сбрасываем на наследование, и файлы получают ровно права папки.
function Set-FolderAcl([string]$path, [string[]]$grants) {
    icacls $path /inheritance:r /grant:r @grants /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls: не удалось выставить права на $path" }
    if (Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue) {
        icacls (Join-Path $path '*') /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls: не удалось сбросить права файлов в $path" }
    }
}

Step "Копирую файлы в $InstallDir"
# Если права сломала прошлая установка, файлы нельзя перезаписать — сначала возвращаем наследование.
if (Test-Path $InstallDir) { icacls $InstallDir /reset /T /C /Q | Out-Null }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $Source '*') -Destination $InstallDir -Recurse -Force

Step 'Права на папку программы: пользователи только читают'
Set-FolderAcl $InstallDir @("${System}:(OI)(CI)F", "${Admins}:(OI)(CI)F", "${Users}:(OI)(CI)RX")

Step "Папка данных ${DataDir}: доступ только у SYSTEM и администраторов"
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
Set-FolderAcl $DataDir @("${System}:(OI)(CI)F", "${Admins}:(OI)(CI)F")
Set-Content -Path (Join-Path $DataDir 'config.json') -Value "{ `"port`": $Port }" -Encoding Ascii

Step 'Регистрирую службу'
$binPath = "`"$InstallDir\Wellbeing.Service.exe`""
if (-not $existing) {
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'Wellbeing — экранное время' `
        -Description 'Учёт экранного времени и дневные лимиты программ.' -StartupType Automatic | Out-Null
}
Set-Service $ServiceName -StartupType Automatic

Step 'Перезапуск при сбое: через 5 с, 5 с, 30 с'
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

Step 'Автозапуск агента для всех пользователей'
Set-ItemProperty -Path $RunKey -Name 'Wellbeing' -Value "`"$InstallDir\Wellbeing.Agent.exe`""

Step 'Ярлык в меню «Пуск»'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($Shortcut)
$lnk.TargetPath = Join-Path $InstallDir 'Wellbeing.Agent.exe'
$lnk.Arguments = '--show'
$lnk.WorkingDirectory = $InstallDir
$lnk.IconLocation = (Join-Path $InstallDir 'Wellbeing.Agent.exe') + ',0'
$lnk.Description = 'Экранное время и лимиты'
$lnk.Save()

Step 'Запускаю службу'
Start-Service $ServiceName
$healthy = $false
for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/health" -TimeoutSec 2 -UseBasicParsing
        $healthy = $health.status -eq 'ok'
    }
    catch { }
}
if (-not $healthy) {
    Write-Warning "Служба не ответила на http://127.0.0.1:$Port/api/health. Смотрите логи: $DataDir\logs"
    exit 1
}

Write-Host ''
Write-Host "Wellbeing $($health.version) установлен и работает." -ForegroundColor Green
Write-Host '  • Служба сама запустит агента в трее в течение ~10 секунд (и при каждом входе в систему).'
Write-Host '  • Открыть окно: значок в трее или «Wellbeing» в меню «Пуск».'
Write-Host "  • Удаление: powershell -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`" (от администратора)."
Write-Host ''
Write-Host 'Для настоящей защиты ваш повседневный аккаунт Windows должен быть без прав администратора — см. README.' -ForegroundColor Yellow
