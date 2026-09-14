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
    git fetch origin --tags --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch не удался" }

    # 1. Тега ещё нет. Переставить существующий нельзя: релиз под ним уже
    #    опубликован, и у тех, кто его поставил, обновление больше не придёт —
    #    номер совпадёт.
    if (git tag --list $tag) { throw "Тег $tag уже существует локально" }
    if (git ls-remote --tags origin $tag) { throw "Тег $tag уже есть на origin" }

    # 2. ГЛАВНАЯ ПРОВЕРКА: всё из ветки разработки доехало в main.
    #    Именно её отсутствие трижды и стоило нам релиза.
    git merge-base --is-ancestor "origin/$Branch" origin/main
    if ($LASTEXITCODE -ne 0) {
        $behind = (git log --oneline "origin/main..origin/$Branch" | Measure-Object -Line).Lines
        throw "origin/main НЕ содержит origin/$Branch (отстаёт на $behind коммит(ов)). " +
              "Сначала влейте pull request, потом ставьте тег."
    }

    # 3. Версия в csproj совпадает с тегом — иначе локальные сборки будут
    #    называть себя не тем, чем их назвал релиз.
    $csproj = git show "origin/main:Oops/Oops.csproj"
    if ($csproj -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
        throw "В Oops.csproj на origin/main версия не $Version"
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
