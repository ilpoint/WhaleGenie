<#
.SYNOPSIS
    把引擎与两个 Windows 工程导出成 ilpoint/WhaleGenie.Core 的一次提交。

.DESCRIPTION
    主仓是唯一源码，库仓是只读镜像：没有一行代码是库仓独有的编辑，全部由本脚本从主仓导出。
    导出三个工程、三套用例、原生库与仓库根的几份文件；再叠上 packaging/core-repo/ 里那几份
    只服务库仓的（根 props、README、发布流水线、公共面文档）。

    默认只生成并报告，不推送；加 -Push 才强推到库仓的 main。镜像仓的 history 是每次同步一条
    提交（不保留主仓的逐条历史），所以推的时候用 --force。

.EXAMPLE
    pwsh build/sync-core-repo.ps1

.EXAMPLE
    pwsh build/sync-core-repo.ps1 -Push
#>
[CmdletBinding()]
param(
    [switch]$Push,
    [string]$Remote = 'https://github.com/ilpoint/WhaleGenie.Core.git'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

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

<#
    PowerShell 5.1 会把原生命令写到 stderr 的每一行包成 NativeCommandError，配上
    ErrorActionPreference = Stop 就是「git 提示一句换行规范化，脚本当场中止」。这里把两个流并成
    字符串，只看退出码：真正失败时把输出打出来再抛。

    参数用显式数组传：`Invoke-Git -C $root ...` 里的 -C 会被 PowerShell 当成这个函数自己的参数。
#>
function Invoke-Git {
    param([string[]]$Arguments)

    # 这一段临时放行：EAP=Stop 时，git 往 stderr 写的任何一行都会变成终止错误。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($code -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "git $($Arguments -join ' ') 失败了（退出码 $code）。"
    }

    return $output
}

if (-not (Test-Path $overlay)) {
    throw "缺少叠加层 $overlay：库仓独有的那几份文件应该在那里。"
}

try {
    $sha = (Invoke-Git -Arguments @('-C', $root, 'rev-parse', '--short', 'HEAD')).Trim()
    $message = "同步自 WhaleGenie@$sha"

    New-Item -ItemType Directory -Force $stage | Out-Null

    Invoke-Git -Arguments (@('-C', $root, 'archive', '--format=zip', "--output=$zip", 'HEAD', '--') + $paths) | Out-Null
    # 用 ZipFile 而不是 Expand-Archive：后者的进度条会把整段输出淹掉。
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)

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

    # core.autocrlf 关掉：镜像只是把这些文件原样装进一次提交，不必再按本机换行习惯改写一遍。
    Invoke-Git -Arguments @('-C', $stage, 'init', '-q', '-b', 'main') | Out-Null
    Invoke-Git -Arguments @('-C', $stage, '-c', 'core.autocrlf=false', 'add', '-A') | Out-Null
    Invoke-Git -Arguments @('-C', $stage, '-c', 'core.autocrlf=false', 'commit', '-q', '-m', $message) | Out-Null

    $files = (Invoke-Git -Arguments @('-C', $stage, 'ls-files') | Measure-Object).Count
    Write-Host "已生成：$message，$files 个文件。"

    if ($Push) {
        Invoke-Git -Arguments @('-C', $stage, 'remote', 'add', 'origin', $Remote) | Out-Null
        Invoke-Git -Arguments @('-C', $stage, 'push', '--force', 'origin', 'main') | Out-Null
        Write-Host "已强推到 $Remote 的 main。"
    }
    else {
        Write-Host "未推送。要推的话加 -Push；这次的树留在 $stage"
        $stage = $null
    }
}
finally {
    if ($stage -and (Test-Path $stage)) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }
}
