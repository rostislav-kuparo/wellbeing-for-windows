#Requires -Version 5.1
<#
.SYNOPSIS
    Собирает дистрибутив Wellbeing в папку dist: веб-интерфейс, тесты, служба и агент
    (self-contained single-file для win-x64) плюс скрипты установки и удаления.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy\build.ps1
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw '.NET SDK не найден. Установите: winget install --id Microsoft.DotNet.SDK.10 -e' }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'Node.js/npm не найден. Установите Node.js 20+.' }

function Invoke-Checked([string]$what, [scriptblock]$command) {
    Write-Host "==> $what" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what — ошибка (код $LASTEXITCODE)" }
}

Push-Location (Join-Path $root 'web')
try {
    # npm ci пересоздаёт node_modules; если они уже есть (например, запущен dev-сервер), хватит npm install.
    if (Test-Path 'node_modules') { Invoke-Checked 'Зависимости веб-интерфейса' { npm install --no-fund --no-audit } }
    else { Invoke-Checked 'Зависимости веб-интерфейса' { npm ci --no-fund --no-audit } }
    Invoke-Checked 'Сборка веб-интерфейса' { npm run build }
}
finally {
    Pop-Location
}

if (-not $SkipTests) {
    Invoke-Checked 'Тесты' { & $dotnet test (Join-Path $root 'Wellbeing.sln') -c Release --nologo }
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$publish = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--nologo',
    '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-o', $dist)

Invoke-Checked 'Публикация службы' { & $dotnet publish (Join-Path $root 'src\Wellbeing.Service\Wellbeing.Service.csproj') @publish }
Invoke-Checked 'Публикация агента' { & $dotnet publish (Join-Path $root 'src\Wellbeing.Agent\Wellbeing.Agent.csproj') @publish }

# Модуль IIS, манифест MapStaticAssets и дубликат загрузчика WebView2 не нужны: служба хостится сама,
# а агент берёт WebView2Loader.dll из папки рядом с exe.
foreach ($extra in 'aspnetcorev2_inprocess.dll', 'Wellbeing.Service.staticwebassets.endpoints.json', 'runtimes') {
    Remove-Item (Join-Path $dist $extra) -Recurse -Force -ErrorAction SilentlyContinue
}

Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') $dist

Write-Host ''
Write-Host "Готово: $dist" -ForegroundColor Green
Get-ChildItem $dist -File | Select-Object Name, @{ n = 'МБ'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host 'Установка (от администратора): powershell -ExecutionPolicy Bypass -File dist\install.ps1'
