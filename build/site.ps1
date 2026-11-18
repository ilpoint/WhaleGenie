[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'site'
$output = Join-Path $repo '_site'

if (-not (Test-Path -LiteralPath $source)) {
    throw "The site sources are missing: $source"
}

# Only the assembled folder is ever removed. Checking the resolved path keeps a moved or mistyped
# variable from turning this into a recursive delete somewhere else.
$repoFull = [IO.Path]::GetFullPath($repo).TrimEnd('\', '/')
$outputFull = [IO.Path]::GetFullPath($output).TrimEnd('\', '/')
if (-not $outputFull.StartsWith($repoFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clear a folder outside the repository: $outputFull"
}

if (Test-Path -LiteralPath $outputFull) {
    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

New-Item -ItemType Directory -Path $outputFull | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $outputFull -Recurse -Force

$assets = Join-Path $outputFull 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

$copies = @(
    @{ From = 'WhaleGenie/Assets/WhaleGenie.png'; To = 'logo.png' },
    @{ From = 'WhaleGenie/Assets/DonateWeChat.png'; To = 'donate-wechat.png' },
    @{ From = 'WhaleGenie/Assets/DonateAlipay.png'; To = 'donate-alipay.png' },
    @{ From = 'doc/images/main-window.png'; To = 'main-window.png' },
    @{ From = 'doc/images/macro-editor.png'; To = 'macro-editor.png' }
)

foreach ($copy in $copies) {
    $from = Join-Path $repo $copy.From
    if (-not (Test-Path -LiteralPath $from)) {
        throw "The site needs a file the repository no longer has: $from"
    }

    Copy-Item -LiteralPath $from -Destination (Join-Path $assets $copy.To) -Force
}

$required = @(
    'index.html',
    'styles.css',
    'site.js',
    'vibe/index.html',
    'assets/logo.png',
    'assets/main-window.png',
    'assets/macro-editor.png',
    'assets/donate-wechat.png',
    'assets/donate-alipay.png',
    'assets/whalegenie-vibe.mp4',
    'assets/whalegenie-vibe-poster.png',
    'assets/whalegenie-og.png'
)

$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $outputFull $_)) }
if ($missing) {
    throw "The assembled site is incomplete: $($missing -join ', ')"
}

Write-Host "Site assembled at $outputFull"
