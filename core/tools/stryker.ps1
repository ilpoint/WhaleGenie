<#
    Runs the mutation baseline one area at a time.

    An area is a small set of files with one meaning, and the tests that decide them. Small areas
    are what keeps a gate useful: a mutant is only judged by the tests that cover it, so an area
    whose files are covered by slow device tests costs minutes per run, while one covered by the
    value tests costs seconds. Reports go to TestResults/Stryker/<area> and are not committed.
#>
param(
    [ValidateSet('formula-json', 'recording', 'expressions-value', 'expressions-writer', 'rules',
        'devices-names', 'devices-colour', 'devices-window', 'devices-files', 'all')]
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
            Name = 'expressions-value'
            Mutate = @(
                '**/Expressions/Value.cs'
                '**/Expressions/ValueRules.cs'
                '**/Expressions/JsonValues.cs'
                '**/Expressions/FormulaJson.cs'
            )
            Filter = 'FullyQualifiedName~ExpressionTests|FullyQualifiedName~FormulaJsonTests|FullyQualifiedName~ValueRulesTests|FullyQualifiedName~ValueDateTests|FullyQualifiedName~ValueSameTests|FullyQualifiedName~ValueImageTests|FullyQualifiedName~WhereColumnTests|FullyQualifiedName~RecordValueTests|FullyQualifiedName~ExpressionArgumentTests|FullyQualifiedName~ListTextTests|FullyQualifiedName~ExpressionContextTests|FullyQualifiedName~ValueTests|FullyQualifiedName~ValueCanonicalTests'
        },
        @{
            Name = 'expressions-writer'
            Mutate = @(
                '**/Expressions/ExpressionWriter.cs'
                '**/Expressions/Expression.cs'
                '**/Expressions/ExpressionNodes.cs'
                '**/Expressions/Formula.cs'
                '**/Expressions/ExpressionCompiler.cs'
            )
            Filter = 'FullyQualifiedName~ExpressionCoverageTests|FullyQualifiedName~FormulaShapeTests|FullyQualifiedName~ExpressionTests|FullyQualifiedName~ExpressionDateLiteralTests|FullyQualifiedName~ExpressionContextTests|FullyQualifiedName~ExpressionArgumentTests|FullyQualifiedName~ExpressionWriterTests|FullyQualifiedName~ExpressionRenameTests|FullyQualifiedName~FormulaTextTests'
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
                '**/Execution/RetryPolicy.cs'
                '**/Execution/WheelPlan.cs'
                '**/Execution/CountPlan.cs'
                '**/Execution/LoopControl.cs'
                '**/Execution/FileOrder.cs'
                '**/Execution/MatchQuota.cs'
                '**/Execution/SwitchModes.cs'
                '**/Execution/CapturePlan.cs'
            )
            Filter = 'FullyQualifiedName~StepMetaTests|FullyQualifiedName~HitSortTests|FullyQualifiedName~HitOrderTests|FullyQualifiedName~StepOutcomeTests|FullyQualifiedName~MacroRunnerTests|FullyQualifiedName~RunLookTests|FullyQualifiedName~WhereColumnTests|FullyQualifiedName~ActionRoutingTests|FullyQualifiedName~ActionPreviewTests|FullyQualifiedName~IrreversibleStepTests|FullyQualifiedName~RetryPolicyTests|FullyQualifiedName~StepChecksTests|FullyQualifiedName~CallBinderTests|FullyQualifiedName~RunDiceTests|FullyQualifiedName~LoopControlTests|FullyQualifiedName~FileOrderTests|FullyQualifiedName~TableColumnsTests|FullyQualifiedName~StepMetaJsonTests|FullyQualifiedName~HitSortOrderTests|FullyQualifiedName~MatchQuotaTests|FullyQualifiedName~StepArgumentsReadTests|FullyQualifiedName~SwitchModesTests|FullyQualifiedName~CapturePlanTests|FullyQualifiedName~WheelPlanTests|FullyQualifiedName~StepTimeoutTests|FullyQualifiedName~CountPlanTests'
        },
        @{
            Name = 'devices-names'
            Mutate = @('**/Devices/KeyNames.cs')
            Filter = 'FullyQualifiedName~KeyNameTests'
        },
        @{
            Name = 'devices-colour'
            Mutate = @(
                '**/Devices/Colour.cs'
                '**/Devices/TextCompare.cs'
                '**/Devices/PixelSearch.cs'
                '**/Devices/ColourReach.cs'
                '**/Devices/ClipboardImage.cs'
                '**/Devices/PngWriter.cs'
                '**/Devices/OcrPreprocess.cs'
                '**/Devices/MousePath.cs'
            )
            Filter = 'FullyQualifiedName~ColourNearnessTests|FullyQualifiedName~TextCompareTests|FullyQualifiedName~TransparentColourTests|FullyQualifiedName~PngWriterTests|FullyQualifiedName~OcrPreprocessTests|FullyQualifiedName~ClipboardImageTests|FullyQualifiedName~MousePathTests|FullyQualifiedName~ImageOrderTests|FullyQualifiedName~UiAnchorTests|FullyQualifiedName~RunDiceTests'
        },
        @{
            Name = 'devices-window'
            Mutate = @(
                '**/Devices/WindowRule.cs'
                '**/Devices/WindowHow.cs'
            )
            Filter = 'FullyQualifiedName~Window|FullyQualifiedName~UiAnchorTests'
        },
        @{
            Name = 'devices-files'
            Mutate = @('**/Devices/DelimitedFile.cs')
            Filter = 'FullyQualifiedName~FileActionTests|FullyQualifiedName~DataActionTests|FullyQualifiedName~SampleCsvTests|FullyQualifiedName~SampleJsonTests|FullyQualifiedName~DelimitedFileTests'
        }
    )

    foreach ($run in $areas) {
        if ($Area -ne 'all' -and $Area -ne $run.Name) {
            continue
        }

        $settings = @{
            project = $project
            'test-projects' = @($tests)
            mutate = $run.Mutate
            reporters = @('cleartext', 'json')
            thresholds = @{ high = 90; low = 80; break = 0 }
        }

        if ($run.Filter.Length -gt 0) {
            $settings['test-case-filter'] = $run.Filter
        }

        $config = @{ 'stryker-config' = $settings }
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
