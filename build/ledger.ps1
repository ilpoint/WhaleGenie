<#
    Rewrites the size ledger from the files that are in the checkout.

    Code is counted in lines and everything else in the bytes Git stores (CRLF folded to LF), which
    is the same measure FileSizeTests and the pre-commit hint use: a row that names a file has to
    say what that file is now. The library lives here as a subtree, so a pull can change the size of
    files this ledger names without anybody here touching them; run this after such a pull and keep
    the row's verdict.

    Rows are dropped when the file is gone or has come back under its check line, because a verdict
    about a file that no longer needs one is a decision nobody reads.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$ledger = Join-Path $root '.githooks/large-file-inventory.tsv'
$codeCheckLine = 300
$documentCheckLine = 20000

function Measure-File {
    param([string] $Path)

    if ($Path -match '\.(cs|axaml|ps1)$') {
        return [System.IO.File]::ReadAllLines($Path).Count
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $size = $bytes.Length
    for ($at = 1; $at -lt $bytes.Length; $at++) {
        if ($bytes[$at - 1] -eq 13 -and $bytes[$at] -eq 10) {
            $size--
        }
    }

    return $size
}

$rows = [System.IO.File]::ReadAllLines($ledger)
$written = [System.Collections.Generic.List[string]]::new()
foreach ($row in $rows) {
    if ($row.Length -eq 0 -or $row[0] -eq '#') {
        $written.Add($row)
        continue
    }

    $parts = $row.Split("`t", 3)
    if ($parts.Length -lt 3) {
        throw "$ledger has a row that is not <metric>`t<path>`t<review>: $row"
    }

    $path = Join-Path $root ($parts[1] -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $metric = Measure-File $path
    $isCode = $parts[1] -match '\.(cs|axaml|ps1)$'
    if ($isCode -and $metric -lt $codeCheckLine) {
        continue
    }

    if (-not $isCode -and $metric -lt $documentCheckLine) {
        continue
    }

    $written.Add("$metric`t$($parts[1])`t$($parts[2])")
}

[System.IO.File]::WriteAllText($ledger, ($written -join "`n") + "`n")
Write-Host "ledger rewritten: $($written.Count) rows"
