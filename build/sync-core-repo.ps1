<#
.SYNOPSIS
    把引擎与两个 Windows 工程导出成 ilpoint/WhaleGenie.Core 的一次提交。

.DESCRIPTION
    主仓是唯一源码，库仓是只读镜像：这里没有一行代码是库仓独有的编辑，全部由本脚本从主仓导出。
    导出的是三个工程、三套用例、原生库与仓库根的几份文件；再叠上 packaging/core-repo/ 里那几份
    只服务库仓的（根 props、README、发布流水线、公共面文档）。

    默认只生成并报告，不推送；加 -Push 才强推到库仓的 main。镜像仓的 history 是每次同步一条
    提交（不保留主仓的逐条历史），所以推的时候用 --force。

.EXAMPLE
    pwsh build/sync-core-repo.ps1
    生成一次，打印会随这次同步变化的文件，不推送。

.EXAMPLE
    pwsh build/sync-core-repo.ps1 -Push
    生成并强推到 ilpoint/WhaleGenie.Core。
#>
[CmdletBinding()]
param(
    [switch]$Push,
    [string]$Remote = 'https://github.com/ilpoint/WhaleGenie.Core.git'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$overlay = Join-Path $root 'packaging/core-repo'
$stage = Join-Path ([IO.Path]::GetTempPath()) ("whalegenie-core-" + [guid]::NewGuid().ToString('N'))
$zip = "$stage.zip"

# 主仓里属于库的那部分。根上的四份文件（global.json、.editorconfig、.gitattributes、LICENSE.txt）
# 两边通用，直接带过去；根 props 与包版本只服务库仓，由叠加层给。
$paths = @(
    'src/WhaleGenie.Core'
    'src/WhaleGenie.GraphicsCapture'
    'src/WhaleGenie.Platform.Windows'
    'tests/WhaleGenie.Core.Tests'
    'tests/WhaleGenie.GraphicsCapture.Tests'
    'tests/WhaleGenie.Platform.Tests'
    'tests/WhaleGenie.TestSupport'
    'third_party/libVIIPER'
    'WhaleGenie.Core.slnx'
    'global.json'
    '.editorconfig'
    '.gitattributes'
    'LICENSE.txt'
)

if (-not (Test-Path $overlay)) {
    throw "缺少叠加层 $overlay：库仓独有的那几份文件应该在那里。"
}

try {
    $sha = (git -C $root rev-parse --short HEAD).Trim()
    $message = "同步自 WhaleGenie@$sha"

    New-Item -ItemType Directory -Force $stage | Out-Null

    $archive = @('archive', '--format=zip', "--output=$zip", 'HEAD', '--') + $paths
    git -C $root @archive
    if ($LASTEXITCODE -ne 0) { throw "git archive 失败（退出码 $LASTEXITCODE）：有路径不在 HEAD 里。" }

    Expand-Archive -Path $zip -DestinationPath $stage -Force

    # 只服务库仓的那几份，覆盖同名文件。
    Get-ChildItem $overlay -Force | ForEach-Object {
        Copy-Item $_.FullName -Destination $stage -Recurse -Force
    }

    # 用例要读的样本在 .gitignore 里，主仓不跟踪，导出时得从工作区拿。
    $samples = Join-Path $root 'testfile'
    if (-not (Test-Path $samples)) {
        throw "缺少 $samples：三套用例要读那里的样本，先从主仓工作区准备。"
    }

    Copy-Item $samples -Destination $stage -Recurse -Force

    git -C $stage init -q -b main
    git -C $stage add -A
    git -C $stage commit -q -m $message
    if ($LASTEXITCODE -ne 0) { throw "提交失败（退出码 $LASTEXITCODE）。" }

    $files = (git -C $stage ls-files | Measure-Object).Count
    Write-Host "已生成：$message，$files 个文件。"

    if ($Push) {
        git -C $stage remote add origin $Remote
        git -C $stage push --force origin main
        if ($LASTEXITCODE -ne 0) { throw "推送失败（退出码 $LASTEXITCODE）。" }
        Write-Host "已强推到 $Remote 的 main。"
    }
    else {
        Write-Host "未推送。要推的话加 -Push。树在 $stage"
        $stage = $null
    }
}
finally {
    if ($stage -and (Test-Path $stage)) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }
}
