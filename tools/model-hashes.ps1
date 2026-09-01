# Считает то, что нужно для описания модели в Core/ModelCatalog.cs:
# SHA-256 и размер файла в том виде, в каком он опубликован.
#
#   pwsh -File tools\model-hashes.ps1 <url> [<url> ...]
#
# Печатает готовые строки C# — их остаётся вставить в ModelCatalog.
#
# Зачем отдельный скрипт: сумма обязана сниматься с файла, который реально
# лежит на сервере, и снять её можно только скачав его. Записывать в каталог
# сумму «на глаз» или брать её из того же ответа, что и файл, — значит не
# проверять ничего (см. раздел «Безопасность» в CLAUDE.md).
#
# Файл на диске не остаётся: он нужен ровно на время подсчёта.

[CmdletBinding()]
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]] $Urls)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

foreach ($url in $Urls) {
    if ($url -notmatch '^https://') { throw "Только https: $url" }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("model-" + [Guid]::NewGuid().ToString("N"))
    try {
        Write-Host "Качаю $url" -ForegroundColor Cyan
        Invoke-WebRequest -Uri $url -OutFile $tmp -MaximumRedirection 5

        $hash  = (Get-FileHash -Path $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        $size  = (Get-Item $tmp).Length
        # Имя на диске — без .gz: архив распаковывается после сверки.
        $name  = [IO.Path]::GetFileName(([Uri]$url).AbsolutePath)
        $gzip  = $name.EndsWith(".gz")
        if ($gzip) { $name = $name.Substring(0, $name.Length - 3) }

        $suffix = if ($gzip) { ", Gzip: true" } else { "" }
        Write-Host ""
        Write-Host ("new ModelFile(""{0}"",`n    ""{1}"",`n    ""{2}"", {3}{4})," -f `
            $name, $url, $hash, $size, $suffix) -ForegroundColor Green
        Write-Host ""
    }
    finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
}
