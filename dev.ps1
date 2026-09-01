# Собрать и запустить свежую сборку одной командой.
#
#   pwsh -ExecutionPolicy Bypass -File dev.ps1
#
# Делает по порядку:
#   1) git pull ветки разработки
#   2) закрывает запущенную копию oops
#   3) dotnet test
#   4) dotnet publish (Release, win-x64, single-file, self-contained)
#   5) запускает получившийся exe
#
# Ключи — чтобы пропустить шаг, когда он не нужен:
#   -NoPull    не трогать git (правки под рукой ещё не закоммичены)
#   -NoTest    без тестов (быстрее на пару десятков секунд)
#   -NoRun     только собрать
#   -Branch    другая ветка вместо prerelease

[CmdletBinding()]
param(
    [string] $Branch = "prerelease",
    [switch] $NoPull,
    [switch] $NoTest,
    [switch] $NoRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Вывод внешних программ (git в первую очередь) читаем как UTF-8.
# По умолчанию консоль Windows разбирает его в кодовой странице 866, и
# сообщение коммита превращается в «╨▓╨╝╨╡╤Б╤В╨╛». На работу это не влияет,
# но строку «Собираю коммит …» печатают ровно затем, чтобы её прочитали.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$RepoRoot = $PSScriptRoot
$Project  = Join-Path $RepoRoot "Oops"
$Exe      = Join-Path $RepoRoot "Oops\bin\Release\net8.0-windows\win-x64\publish\oops.exe"

function Step([string] $text) { Write-Host "`n=== $text" -ForegroundColor Cyan }

# dotnet не роняет скрипт сам по себе: PowerShell считает провалом только
# ненулевой код возврата внешней программы, а $ErrorActionPreference на него
# не распространяется. Без явной проверки сборка падала, а скрипт бодро шёл
# дальше и запускал ПРОШЛЫЙ exe — разница незаметная и очень обидная.
# ${what}, а не $what: двоеточие сразу после имени PowerShell читает как
# разделитель области видимости ($env:PATH) — строка не разбирается вовсе,
# и скрипт падает ещё до первой команды.
function Assert-LastExitCode([string] $what) {
    if ($LASTEXITCODE -ne 0) { throw "${what}: код возврата $LASTEXITCODE" }
}

Push-Location $RepoRoot
try {
    if (-not $NoPull) {
        Step "Обновляю $Branch"
        git checkout $Branch;            Assert-LastExitCode "git checkout"
        git pull origin $Branch;         Assert-LastExitCode "git pull"
    }
    Write-Host ("Собираю коммит: " + (git log --oneline -1)) -ForegroundColor DarkGray

    # Запущенная копия держит oops.exe открытым, и publish падает с MSB3027
    # («Could not copy … Exceeded retry count of 10»).
    $running = Get-Process -Name "oops" -ErrorAction SilentlyContinue
    if ($running) {
        Step "Закрываю запущенный oops (PID $($running.Id -join ', '))"
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    if (-not $NoTest) {
        Step "Тесты"
        dotnet test;                     Assert-LastExitCode "dotnet test"
    }

    Step "Сборка"
    dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    Assert-LastExitCode "dotnet publish"

    if (-not (Test-Path $Exe)) { throw "Не нашёл собранный exe: $Exe" }

    if ($NoRun) {
        Write-Host "`nГотово: $Exe" -ForegroundColor Green
    }
    else {
        Step "Запуск"
        Start-Process $Exe
        Write-Host "oops запущен — иконка в трее, рядом с часами." -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
