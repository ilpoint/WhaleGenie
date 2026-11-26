$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'TestResults/Stryker/run'
New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $work
try {
    dotnet tool restore | Out-Host

    # Project context, not solution context: the work folder has no solution beside it, and the
    # two paths are absolute so the config can be machine-independent.
    $config = @{
        'stryker-config' = @{
            project = Join-Path $root 'src/WhaleGenie.Core/WhaleGenie.Core.csproj'
            'test-projects' = @(Join-Path $root 'tests/WhaleGenie.Core.Tests/WhaleGenie.Core.Tests.csproj')
            mutate = @('**/FormulaJson.cs')
            'test-case-filter' = 'FullyQualifiedName~FormulaJsonTests'
            reporters = @('cleartext', 'json')
            thresholds = @{ high = 90; low = 80; break = 0 }
        }
    }
    $configPath = Join-Path $work 'stryker-config.json'
    $config | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding utf8
    $output = Join-Path $root 'TestResults/Stryker/formula-json'
    dotnet tool run dotnet-stryker -f $configPath --configuration Release -c 4 -O $output
    if ($LASTEXITCODE -ne 0) {
        throw 'Stryker failed for FormulaJson'
    }
}
finally {
    Pop-Location
}
