<#
.SYNOPSIS
    把 Viktor 打成两个便携包：一个要机器上有 .NET 桌面运行时，一个什么都不要。
.DESCRIPTION
    两种包的内容只差运行时，目录形状是一样的：外层一个 Viktor 文件夹，里面是 exe、
    清单和装 dll 的 lib 文件夹。用户解开压缩包先看到 exe，不用在一堆 dll 里翻。

    用法：
        pwsh build/package.ps1 -Tag v0.01
        pwsh build/package.ps1 -Tag v0.01 -Version 0.01 -Rid win-x64

    产物落在 -OutDir（默认 dist，已在 .gitignore 里）：Viktor-<标签>-<架构>.zip 和
    Viktor-<标签>-<架构>-standalone.zip。打包过程中用的中间目录结束时会被删掉。
#>
[CmdletBinding()]
param(
    # 压缩包名里的版本，比如 v0.01。
    [Parameter(Mandatory = $true)][string]$Tag,

    # exe 里的版本号，默认就是标签去掉开头的 v；标签带后缀时另填一个。
    [string]$Version,

    [string[]]$Rid = @('win-x64', 'win-x86'),

    [string]$OutDir = 'dist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Version) {
    $Version = $Tag -replace '^v', ''
}

$project = Join-Path $PSScriptRoot '..\Viktor\Viktor.csproj'
$OutDir = Join-Path $PWD $OutDir

# 运行时自己的这几个文件必须在 exe 旁边：宿主是先找 hostpolicy、再找 coreclr、再由
# coreclr 找 JIT 和核心库的，都按 "exe 所在目录" 找，放进 lib 就找不到，程序连错误
# 都报不出来。其余 dll 一律进 lib。
$hostFiles = @(
    'hostfxr.dll'
    'hostpolicy.dll'
    'coreclr.dll'
    'clrjit.dll'
    'System.Private.CoreLib.dll'
    'mscorrc.dll'
    'clretwrc.dll'
    'vcruntime140_cor3.dll'
    'mscordaccore.dll'
    'mscordaccore_amd64_amd64_*.dll'
    'mscordbi.dll'
)

function Test-HostFile([string]$Name) {
    foreach ($pattern in $hostFiles) {
        if ($Name -like $pattern) {
            return $true
        }
    }

    return $false
}

function Remove-Folder([string]$Path) {
    if (Test-Path $Path) {
        [System.IO.Directory]::Delete($Path, $true)
    }
}

<#
    把一次 dotnet publish 的结果摆成便携包的形状。清单（Viktor.deps.json）里每一项的
    写法决定运行时装到哪儿找程序集和原生库，所以 dll 搬家时清单得跟着改：留在 exe
    旁边的保持原名，进 lib 的写成 lib/<文件名>。
#>
function New-PortableFolder([string]$Published, [string]$Package) {
    $app = Join-Path $Package 'Viktor'
    $lib = Join-Path $app 'lib'
    [System.IO.Directory]::CreateDirectory($lib) | Out-Null

    Copy-Item -Path (Join-Path $Published '*') -Destination $app -Recurse

    # 原生库自带的调试符号，比程序本身还大（Skia 一份 80 MB），用户用不上。
    Get-ChildItem $app -Recurse -Filter *.pdb | ForEach-Object { $_.Delete() }

    $moved = @(Get-ChildItem $Published -File |
        Where-Object { $_.Extension -eq '.dll' -and $_.Name -ne 'Viktor.dll' -and -not (Test-HostFile $_.Name) } |
        ForEach-Object { $_.Name })

    $deps = Join-Path $app 'Viktor.deps.json'
    $json = [System.IO.File]::ReadAllText($deps)
    $json = [regex]::Replace($json, '"((?:[^"]*/)?)([^"/]+\.dll)":', {
            param($match)
            if ($moved -contains $match.Groups[2].Value) {
                '"lib/' + $match.Groups[2].Value + '":'
            }
            else {
                $match.Value
            }
        })
    [System.IO.File]::WriteAllText($deps, $json)

    Get-ChildItem $app -File | Where-Object { $moved -contains $_.Name } | Move-Item -Destination $lib

    return $app
}

[System.IO.Directory]::CreateDirectory($OutDir) | Out-Null

foreach ($architecture in $Rid) {
    $published = Join-Path $OutDir "publish-$architecture"
    $package = Join-Path $OutDir "package-$architecture"
    Remove-Folder $published
    Remove-Folder $package

    # 框架依赖和自带运行时各发布一次；关掉调试符号，pdb 比程序还大。
    foreach ($selfContained in @($false, $true)) {
        $target = Join-Path $published $(if ($selfContained) { 'standalone' } else { 'framework' })
        $arguments = @(
            'publish', $project
            '-c', 'Release', '-r', $architecture
            "--self-contained", $selfContained.ToString().ToLowerInvariant()
            '-p:DebugType=none'
            "-p:Version=$Version"
            '--nologo'
            '-o', $target
        )

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish ($architecture, self-contained=$selfContained) 失败，退出码 $LASTEXITCODE"
        }
    }

    $framework = New-PortableFolder (Join-Path $published 'framework') (Join-Path $package 'framework')
    $standalone = New-PortableFolder (Join-Path $published 'standalone') (Join-Path $package 'standalone')

    $frameworkZip = Join-Path $OutDir "Viktor-$Tag-$architecture.zip"
    $standaloneZip = Join-Path $OutDir "Viktor-$Tag-$architecture-standalone.zip"
    foreach ($zip in @($frameworkZip, $standaloneZip)) {
        if (Test-Path $zip) {
            [System.IO.File]::Delete($zip)
        }
    }

    Compress-Archive -Path $framework -DestinationPath $frameworkZip
    Compress-Archive -Path $standalone -DestinationPath $standaloneZip

    foreach ($zip in @($frameworkZip, $standaloneZip)) {
        $item = Get-Item $zip
        "{0}  {1:N1} MB" -f $item.Name, ($item.Length / 1MB)
    }

    Remove-Folder $published
    Remove-Folder $package
}
