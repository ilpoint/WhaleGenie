<#
    Measures the two startup numbers that can be taken without showing the program:

    - the headless start: process start to the end of the MCP self-check (--mcp-doctor), which walks
      the composition root and registers every tool;
    - the shell page: the built page served from disk and loaded in headless Edge, DOMContentLoaded
      and load.

    Neither includes WebView2 or a first frame drawn on a desktop; those need the program itself and
    are described in docs/architecture.md.
#>
param([switch] $SkipDoctor, [switch] $SkipPage)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $SkipDoctor) {
    $exe = Join-Path $root 'src/WhaleGenie/bin/Release/net10.0-windows10.0.19041/WhaleGenie.exe'
    if (-not (Test-Path $exe)) {
        throw "Build the program first: $exe is not there."
    }

    $log = Join-Path $env:TEMP 'whalegenie-doctor.txt'
    foreach ($round in 1..2) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $arguments = @{ FilePath = $exe; ArgumentList = '--mcp-doctor'; Wait = $true; PassThru = $true; WindowStyle = 'Hidden'; RedirectStandardOutput = $log; RedirectStandardError = "$log.err" }
        $process = Start-Process @arguments
        $watch.Stop()
        "headless start $($round): $([math]::Round($watch.Elapsed.TotalMilliseconds)) ms, exit $($process.ExitCode)"
    }
}

if (-not $SkipPage) {
    Push-Location (Join-Path $root 'webui')
    try {
        node tools/measure-page.mjs
    }
    finally {
        Pop-Location
    }
}