#Requires -Version 5.1
<#
.SYNOPSIS
    Полностью удаляет Wellbeing. Это аварийный выход: каждый шаг выполняется независимо от остальных,
    поэтому скрипт работает и после частичной установки, и когда служба не отвечает.
.PARAMETER KeepData
    Оставить статистику и настройки (C:\ProgramData\Wellbeing).
#>
param([switch]$KeepData)

$ErrorActionPreference = 'Continue'

# Без прав администратора — перезапускаемся с повышением (UAC) в отдельном окне.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Нужны права администратора — запрашиваю повышение (UAC)…'
    $arguments = "-NoProfile -NoExit -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($KeepData) { $arguments += ' -KeepData' }
    try { Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments }
    catch { Write-Warning 'Удаление отменено: права администратора не получены.' }
    exit
}

$ServiceName = 'WellbeingSvc'
$InstallDir = Join-Path $env:ProgramFiles 'Wellbeing'
$DataDir = Join-Path $env:ProgramData 'Wellbeing'
$RunKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$Shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Wellbeing.lnk'

function Step([string]$text, [scriptblock]$action) {
    Write-Host "-> $text" -ForegroundColor Cyan
    try { & $action } catch { Write-Warning "   $($_.Exception.Message)" }
}

function Remove-Folder([string]$path) {
    if (-not (Test-Path $path)) { return }
    for ($i = 0; $i -lt 5; $i++) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $path)) { return }
        # Права могли быть испорчены — возвращаем наследование от родительской папки и пробуем снова.
        icacls $path /reset /T /C /Q | Out-Null
        Start-Sleep -Seconds 1
    }
    if (Test-Path $path) { Write-Warning "   Не удалось удалить $path — удалите вручную после перезагрузки." }
}

# Скрипт может лежать в удаляемой папке — уходим из неё.
Set-Location $env:SystemRoot

Step 'Отключаю службу и её автоперезапуск' {
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Set-Service $ServiceName -StartupType Disabled
        Start-Process sc.exe -ArgumentList "failure $ServiceName reset= 0 actions= `"`"" -Wait -NoNewWindow
    }
}

Step 'Останавливаю службу' {
    $service = Get-Service $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }
}

Step 'Завершаю процессы Wellbeing' {
    Get-Process Wellbeing.Service, Wellbeing.Agent -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Step 'Удаляю службу' {
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { sc.exe delete $ServiceName | Out-Null }
}

Step 'Убираю автозапуск агента' {
    if (Get-ItemProperty -Path $RunKey -Name 'Wellbeing' -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $RunKey -Name 'Wellbeing'
    }
}

Step 'Удаляю ярлык из меню «Пуск»' {
    if (Test-Path $Shortcut) { Remove-Item $Shortcut -Force }
}

Step "Удаляю файлы программы ($InstallDir)" { Remove-Folder $InstallDir }

if ($KeepData) {
    Write-Host "-> Данные оставлены: $DataDir" -ForegroundColor Cyan
}
else {
    Step "Удаляю данные ($DataDir)" { Remove-Folder $DataDir }
}

Step 'Удаляю данные окна (WebView2) и логи агента в профилях пользователей' {
    $profiles = Get-CimInstance Win32_UserProfile | Where-Object { -not $_.Special -and $_.LocalPath }
    foreach ($p in $profiles) { Remove-Folder (Join-Path $p.LocalPath 'AppData\Local\Wellbeing') }
}

Write-Host ''
if ((Get-Service $ServiceName -ErrorAction SilentlyContinue) -or (Test-Path $InstallDir)) {
    Write-Warning 'Удаление завершено не полностью — см. предупреждения выше. Перезагрузите компьютер и запустите скрипт ещё раз.'
}
else {
    Write-Host 'Wellbeing удалён.' -ForegroundColor Green
}
