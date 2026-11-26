param(
    [ValidateSet('domain', 'editing', 'all')]
    [string]$Target = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'TestResults/Stryker/run'
New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $work
try {
    dotnet tool restore | Out-Host

    # A run is a project, the tests that decide it, the files to mutate, and the law tests that
    # should be enough to kill every mutant. The config is generated here because the paths are
    # absolute and the file must not carry one machine's checkout.
    $runs = @(
        @{
            Name = 'domain'
            Project = Join-Path $root 'src/WhaleGenie.Domain/WhaleGenie.Domain.csproj'
            Tests = @(Join-Path $root 'tests/WhaleGenie.Domain.Tests/WhaleGenie.Domain.Tests.csproj')
            Mutate = @('**/StepParameter.File.cs')
            Filter = 'FullyQualifiedName~StepShapeRoundTripTests|FullyQualifiedName~StepPackageShapeTests'
            Output = Join-Path $root 'TestResults/Stryker/domain'
        },
        @{
            Name = 'editing'
            Project = Join-Path $root 'src/WhaleGenie.Editing/WhaleGenie.Editing.csproj'
            Tests = @(
                Join-Path $root 'tests/WhaleGenie.Editor.Tests/WhaleGenie.Editor.Tests.csproj'
                Join-Path $root 'tests/WhaleGenie.Tests/WhaleGenie.Tests.csproj'
            )
            Mutate = @(
                '**/RegionRowViewModel.cs'
                '**/CallArgumentRowViewModel.cs'
                '**/EditorValueParts.cs'
            )
            Filter = 'FullyQualifiedName~ShapeDraftTests|FullyQualifiedName~EditorValuePartsTests|FullyQualifiedName~HeadlessActionSessionTests'
            Output = Join-Path $root 'TestResults/Stryker/editing'
        }
    )

    foreach ($run in $runs) {
        if ($Target -ne 'all' -and $Target -ne $run.Name) {
            continue
        }

        $config = @{
            'stryker-config' = @{
                project = $run.Project
                'test-projects' = $run.Tests
                mutate = $run.Mutate
                'test-case-filter' = $run.Filter
                reporters = @('cleartext', 'json')
                thresholds = @{ high = 90; low = 80; break = 0 }
            }
        }
        $configPath = Join-Path $work ($run.Name + '-stryker-config.json')
        $config | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding utf8
        dotnet tool run dotnet-stryker -f $configPath --configuration Release -c 4 -O $run.Output
        if ($LASTEXITCODE -ne 0) {
            throw "Stryker failed for $($run.Name)"
        }
    }
}
finally {
    Pop-Location
}
