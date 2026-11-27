<#
    Measures the startup numbers, each on its own switch:

    - the headless start: process start to the end of the MCP self-check (--mcp-doctor);
    - the shell page: the built page served from disk and loaded in headless Edge;
    - the desktop start (-Desktop): the real program, first frame and macro list, and opening a
      macro to a form you could type in. This one shows a window and closes it again, and it needs
      a Debug build — that is the only build that opens the CDP door (9223).
#>
param([switch] $SkipDoctor, [switch] $SkipPage, [switch] $Desktop, [int] $Rounds = 3)

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

if ($Desktop) {
    $debug = Join-Path $root 'src/WhaleGenie/bin/Debug/net10.0-windows10.0.19041/WhaleGenie.exe'
    if (-not (Test-Path $debug)) {
        throw "Build Debug first: $debug is not there. Only a Debug build opens the CDP door."
    }

    Push-Location (Join-Path $root 'webui')
    try {
        $env:WHALEGENIE_EXE = $debug
        $env:WHALEGENIE_ROUNDS = $Rounds
        node tools/measure-desktop.mjs
    }
    finally {
        Remove-Item Env:WHALEGENIE_EXE, Env:WHALEGENIE_ROUNDS -ErrorAction SilentlyContinue
        Pop-Location
    }
}