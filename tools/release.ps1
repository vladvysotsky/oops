# Ставит тег релиза — и сначала проверяет, что ставить его есть на что.
#
#   pwsh -File tools\release.ps1 2.1.1
#
# Проверки не формальность: три тега подряд уехали на старый коммит, потому
# что «git tag origin/main» выполняли ДО того, как влили pull request. Тег при
# этом встаёт молча, CI бодро собирает прошлую версию и публикует её под новым
# номером. Скрипт отказывается это делать.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Version,                       # без «v»: 2.1.1

    [string] $Branch = "prerelease"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $RepoRoot
try {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Версия должна быть вида 2.1.1, получено «$Version»"
    }
    $tag = "v$Version"

    Write-Host "Обновляю сведения с origin..." -ForegroundColor Cyan
    # --force обязателен: если локальный тег расходится с удалённым (остался от
    # переставленного когда-то тега), обычный fetch отказывается его трогать и
    # возвращает ошибку целиком — из-за одного постороннего тега не выполнялся
    # весь релиз. Истина здесь у origin, локальные копии тегов значения не имеют.
    git fetch origin --tags --prune --force
    if ($LASTEXITCODE -ne 0) { throw "git fetch не удался" }

    # 1. Тега ещё нет. Переставить существующий нельзя: релиз под ним уже
    #    опубликован, и у тех, кто его поставил, обновление больше не придёт —
    #    номер совпадёт.
    if (git tag --list $tag) { throw "Тег $tag уже существует локально" }
    if (git ls-remote --tags origin $tag) { throw "Тег $tag уже есть на origin" }

    # 2. ГЛАВНАЯ ПРОВЕРКА: версия в csproj на origin/main совпадает с тегом.
    #    Она и ловит ту самую ошибку — тег до мержа: на невлитом main лежит
    #    версия прошлого релиза, и совпасть она не может по построению.
    #
    #    git show отдаёт МАССИВ строк, а -notmatch на массиве не булев ответ,
    #    а фильтр: возвращает все не совпавшие строки, и непустой список в if
    #    всегда истина. Склеиваем в одну строку — иначе проверка врёт всегда.
    $csproj = (git show "origin/main:Oops/Oops.csproj") -join "`n"
    if ($csproj -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
        $actual = if ($csproj -match '<Version>([^<]+)</Version>') { $Matches[1] } else { "не найдена" }
        throw "На origin/main версия $actual, а тег просят $Version. " +
              "Похоже, pull request ещё не влит."
    }

    # 3. Ветка разработки доехала в main — ПРЕДУПРЕЖДЕНИЕ, а не отказ.
    #    Отказ здесь загонял в тупик: любая правка самого этого скрипта уводит
    #    prerelease вперёд, и выпустить релиз становится нельзя, пока не влит
    #    ещё один pull request — про сам инструмент выпуска.
    git merge-base --is-ancestor "origin/$Branch" origin/main
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nВНИМАНИЕ: origin/main не содержит эти коммиты из origin/${Branch}:" -ForegroundColor Yellow
        git log --oneline "origin/main..origin/$Branch"
        Write-Host "В релиз они не попадут.`n" -ForegroundColor Yellow
    }

    # 4. Описание релиза на месте — иначе в релизе останется одна строка
    #    «Full Changelog» (автогенерация собирает заметки из смерженных PR).
    $notes = "docs/release-notes/$tag.md"
    git cat-file -e "origin/main:$notes" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Нет $notes на origin/main" }

    $commit = git rev-parse --short origin/main
    Write-Host "`nВсё сходится. Тег $tag на origin/main ($commit)." -ForegroundColor Green
    Write-Host (git log --oneline -1 origin/main) -ForegroundColor DarkGray

    git tag -a $tag origin/main -m "oops $Version"
    if ($LASTEXITCODE -ne 0) { throw "не удалось создать тег" }

    git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw "не удалось отправить тег" }

    Write-Host "`nГотово. Сборка релиза запущена:" -ForegroundColor Green
    Write-Host "https://github.com/vladvysotsky/oops/actions" -ForegroundColor Cyan
}
finally {
    Pop-Location
}
