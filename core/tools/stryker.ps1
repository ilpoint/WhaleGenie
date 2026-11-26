<#
    Runs the mutation baseline one area at a time.

    Stryker mutates a project against the tests that decide it: an area is the files to mutate, the
    tests that should be enough to kill every mutant, and the report to leave behind. Reports go to
    TestResults/Stryker/<area> and are not committed; the numbers that matter are written down in
    the program's testing notes.
#>
param(
    [ValidateSet('formula-json', 'recording', 'expressions', 'rules', 'devices', 'all')]
    [string]$Area = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'TestResults/Stryker/run'
New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $work
try {
    dotnet tool restore | Out-Host

    # Project context, not solution context: the work folder has no solution beside it, and the
    # two paths are absolute so the config can be machine-independent.
    $project = Join-Path $root 'src/WhaleGenie.Core/WhaleGenie.Core.csproj'
    $tests = Join-Path $root 'tests/WhaleGenie.Core.Tests/WhaleGenie.Core.Tests.csproj'

    # Expressions: the value model, the tree, the reader and the files a formula is written in.
    # The context tests are in the filter because the date and dice answers are theirs.
    $expressionTests = @(
        'FullyQualifiedName~ExpressionTests'
        'FullyQualifiedName~FormulaJsonTests'
        'FullyQualifiedName~ValueRulesTests'
        'FullyQualifiedName~ValueDateTests'
        'FullyQualifiedName~ValueSameTests'
        'FullyQualifiedName~ValueImageTests'
        'FullyQualifiedName~FormulaShapeTests'
        'FullyQualifiedName~ExpressionCoverageTests'
        'FullyQualifiedName~ExpressionArgumentTests'
        'FullyQualifiedName~ExpressionDateLiteralTests'
        'FullyQualifiedName~ExpressionContextTests'
        'FullyQualifiedName~WhereColumnTests'
        'FullyQualifiedName~ListTextTests'
        'FullyQualifiedName~RecordValueTests'
    ) -join '|'

    # Rules: the decisions a run makes without a device — retry, order, step settings, calls.
    $ruleTests = @(
        'FullyQualifiedName~StepMetaTests'
        'FullyQualifiedName~HitSortTests'
        'FullyQualifiedName~HitOrderTests'
        'FullyQualifiedName~StepOutcomeTests'
        'FullyQualifiedName~MacroRunnerTests'
        'FullyQualifiedName~RunLookTests'
        'FullyQualifiedName~WhereColumnTests'
    ) -join '|'

    # Devices: the tools the engine ships that do not talk to a machine.
    $deviceTests = @(
        'FullyQualifiedName~KeyNameTests'
        'FullyQualifiedName~TextCompareTests'
        'FullyQualifiedName~MousePathTests'
        'FullyQualifiedName~TransparentColourTests'
        'FullyQualifiedName~ColourNearnessTests'
        'FullyQualifiedName~PngWriterTests'
        'FullyQualifiedName~OcrPreprocessTests'
        'FullyQualifiedName~ClipboardImageTests'
        'FullyQualifiedName~ImageOrderTests'
        'FullyQualifiedName~UiAnchorTests'
    ) -join '|'

    $areas = @(
        @{
            Name = 'formula-json'
            Mutate = @('**/FormulaJson.cs')
            Filter = 'FullyQualifiedName~FormulaJsonTests'
        },
        @{
            Name = 'recording'
            Mutate = @('**/Recording/RecordingTranslator.cs')
            Filter = 'FullyQualifiedName~RecordingTests'
        },
        @{
            Name = 'expressions'
            Mutate = @(
                '**/Expressions/Value.cs'
                '**/Expressions/ValueRules.cs'
                '**/Expressions/Formula.cs'
                '**/Expressions/FormulaJson.cs'
                '**/Expressions/JsonValues.cs'
                '**/Expressions/Expression.cs'
                '**/Expressions/ExpressionCompiler.cs'
                '**/Expressions/ExpressionWriter.cs'
                '**/Expressions/ExpressionNodes.cs'
            )
            Filter = $expressionTests
        },
        @{
            Name = 'rules'
            Mutate = @(
                '**/Execution/StepMeta.cs'
                '**/Execution/HitSort.cs'
                '**/Execution/HitOrder.cs'
                '**/Execution/StepChecks.cs'
                '**/Execution/StepArguments.cs'
                '**/Execution/ScreenMath.cs'
                '**/Execution/CallBinder.cs'
                '**/Execution/StepTimeout.cs'
                '**/Execution/ShapeWords.cs'
                '**/Execution/TextRule.cs'
                '**/Execution/TableColumns.cs'
                '**/Execution/Templates.cs'
                '**/Execution/StepRows.cs'
            )
            Filter = $ruleTests
        },
        @{
            Name = 'devices'
            Mutate = @(
                '**/Devices/KeyNames.cs'
                '**/Devices/TextCompare.cs'
                '**/Devices/MousePath.cs'
                '**/Devices/PixelSearch.cs'
                '**/Devices/PngWriter.cs'
                '**/Devices/OcrPreprocess.cs'
                '**/Devices/DelimitedFile.cs'
                '**/Devices/ColourReach.cs'
                '**/Devices/WindowHow.cs'
                '**/Devices/WindowRule.cs'
                '**/Devices/Colour.cs'
                '**/Devices/ClipboardImage.cs'
            )
            Filter = $deviceTests
        }
    )

    foreach ($run in $areas) {
        if ($Area -ne 'all' -and $Area -ne $run.Name) {
            continue
        }

        $config = @{
            'stryker-config' = @{
                project = $project
                'test-projects' = @($tests)
                mutate = $run.Mutate
                'test-case-filter' = $run.Filter
                reporters = @('cleartext', 'json')
                thresholds = @{ high = 90; low = 80; break = 0 }
            }
        }
        $configPath = Join-Path $work ($run.Name + '-stryker-config.json')
        $config | ConvertTo-Json -Depth 6 | Set-Content -Path $configPath -Encoding utf8
        $output = Join-Path $root ('TestResults/Stryker/' + $run.Name)
        dotnet tool run dotnet-stryker -f $configPath --configuration Release -c 4 -O $output
        if ($LASTEXITCODE -ne 0) {
            throw "Stryker failed for $($run.Name)"
        }
    }
}
finally {
    Pop-Location
}
