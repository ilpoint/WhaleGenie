<#
.SYNOPSIS
    把 WhaleGenie 打成两个便携包：一个要机器上有 .NET 桌面运行时，一个什么都不要。
.DESCRIPTION
    两种包的内容只差运行时，目录形状是一样的：外层一个 WhaleGenie 文件夹，里面只有一个
    WhaleGenie.exe、随程序走的 .playwright 驱动、手册与许可，原生库收在 lib 里。托管的程序集
    都在 exe 里，没有语言资源文件夹，也没有 pdb；用户解开压缩包先看到 exe，不用在一堆文件里翻。

    只做 64 位：这个程序靠的 OpenCV（找图找色）和 Playwright 驱动（浏览器动作）都只有 64 位
    一份，32 位的包装上也是坏的，所以不再出。

    用法：
        pwsh build/package.ps1 -Tag v0.01
        pwsh build/package.ps1 -Tag v0.01 -Version 0.01 -Rid win-x64

    产物落在 -OutDir（默认 dist，已在 .gitignore 里）：WhaleGenie-<标签>-<架构>.zip 和
    WhaleGenie-<标签>-<架构>-standalone.zip。打包过程中用的中间目录结束时会被删掉。
#>
[CmdletBinding()]
param(
    # 压缩包名里的版本，比如 v0.01。
    [Parameter(Mandatory = $true)][string]$Tag,

    # exe 里的版本号，默认就是标签去掉开头的 v；标签带后缀时另填一个。
    [string]$Version,

    [string[]]$Rid = @('win-x64'),

    [string]$OutDir = 'dist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Version) {
    $Version = $Tag -replace '^v', ''
}

# 版本号原样交给 dotnet publish 的 -p:Version：exe 的文件属性、NuGet 的还原都按它来。
# 一旦它不像版本号（手滑写成 V0.01，或者带中文），还原会当场失败，而 MSBuild 只报
# "RestoreTask 返回 false but did not log an error"，看不出是哪一步错了，所以在这里先说清楚。
$rawVersion = $Version
$Version = $Version -replace '^[vV]', ''
if ($Version -notmatch '^\d+(\.\d+){0,3}(-[0-9A-Za-z.]+)?$') {
    throw "版本号 '$rawVersion' 不像版本号，写成 0.01 或 1.2.3 这样的数字。"
}

$project = Join-Path $PSScriptRoot '..\WhaleGenie\WhaleGenie.csproj'
$OutDir = Join-Path $PWD $OutDir

# 单文件发布把托管程序集收进 exe，发布目录里剩下的 dll 全是原生库。Windows 和这些库自己
# 都按文件名找它们，程序启动时把 lib 交给 Windows 的查找路径（Program.HookLibraryFolder），
# 所以它们统一进 lib；exe 旁边不再留 dll，用户打开文件夹只看得到程序本身。

function Remove-Folder([string]$Path) {
    if (Test-Path $Path) {
        [System.IO.Directory]::Delete($Path, $true)
    }
}

<#
    Playwright 的驱动是按 PlaywrightPlatform（在根 Directory.Build.props 里定成 win-x64）挑一份
    node 抄出来的。这个属性一旦失效——名字被上游改了、驱动换了目录形状——包里就会多带别的平台
    的 node：一份都用不上，包还大一倍，而拿到 zip 的人看不出来。所以摆好包之后在这里拦一道：
    node 下面只有一个平台目录，里面要有 node.exe。平台目录叫什么不写死，驱动换名字不会误报。
#>
function Assert-OnePlatformDriver([string]$App) {
    $node = Join-Path $App '.playwright\node'
    if (-not (Test-Path $node)) {
        throw "包里没有 .playwright\node：Playwright 的驱动没被抄出来，先看 dotnet publish 那一句。"
    }

    $platforms = @(Get-ChildItem $node -Directory)
    if ($platforms.Count -ne 1) {
        $names = ($platforms | ForEach-Object { $_.Name }) -join ', '
        throw ("包里带了 $($platforms.Count) 个平台的 node（$names）：PlaywrightPlatform " +
            "本该只留 Windows 那一份，先看清包为什么变大。")
    }

    if (-not (Test-Path (Join-Path $platforms[0].FullName 'node.exe'))) {
        throw ("包里的 .playwright\node\$($platforms[0].Name) 没有 node.exe：驱动的形状变了，" +
            "浏览器动作会在用户那里报 Driver not found。")
    }
}

<#
    把一次 dotnet publish 的结果摆成便携包的形状。单文件发布已经把托管程序集收进 exe，
    这里只把剩下的原生库收进 lib；单文件发布没有 deps.json，运行时也不再靠它找托管程序集。
#>
function New-PortableFolder([string]$Published, [string]$Package) {
    $app = Join-Path $Package 'WhaleGenie'
    $lib = Join-Path $app 'lib'

    [System.IO.Directory]::CreateDirectory($app) | Out-Null
    Copy-Item -Path (Join-Path $Published '*') -Destination $app -Recurse

    # 单文件发布本身已经会把原生库放进 lib；这里兜住更早的发布结果或发布目标失效的场合。
    [System.IO.Directory]::CreateDirectory($lib) | Out-Null

    # 原生库自带的调试符号，比程序本身还大（Skia 一份 80 MB），用户用不上。单文件发布时
    # 项目里的 DropPublishSymbols 已经把它们从清单里摘掉，这里再兜一次底。
    Get-ChildItem $app -Recurse -Filter *.pdb | ForEach-Object { $_.Delete() }

    Assert-OnePlatformDriver $app

    # 自己的许可、署名和第三方说明必须跟着程序走：分发时不给出这三样，等于没写。
    foreach ($document in @('LICENSE.txt', 'NOTICE', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -Path (Join-Path $PSScriptRoot "..\$document") -Destination $app
    }

    # 驱动级输入那份虚拟 USB 库是别人的，而且按 GPL-3.0 授权：它自己的许可说明也要跟着它的
    # dll 一起走，改个名字放在包根，一眼能看出是谁的。dll 本身由下面那条"原生库一律进 lib"
    # 的规则搬走，这里只多这一份说明。
    Copy-Item -Path (Join-Path $PSScriptRoot '..\third_party\libVIIPER\licenses.txt') `
        -Destination (Join-Path $app 'libVIIPER-LICENSES.txt')

    # 手册也要跟着走：帮助菜单的「快速上手」打开的是包里的这一份（WhaleGenie/Execution/Manual.cs），
    # 装好程序的人手上没有仓库可翻，也不一定有网。它按 exe 旁边的 doc\manual.md 找，路径别改。
    $packagedManual = Join-Path $app 'doc'
    [System.IO.Directory]::CreateDirectory($packagedManual) | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot '..\doc\manual.md') -Destination $packagedManual

    # 单文件之外只剩原生库（OpenCV、ffmpeg、Skia、HarfBuzz、uiohook、libVIIPER），全部进 lib。
    Get-ChildItem $app -File -Filter *.dll | Move-Item -Destination $lib

    # 包根再出现 dll 就说明单文件发布没生效：用户又会回到"几十个文件"的样子，所以在这里拦住。
    if (Get-ChildItem $app -File -Filter *.dll) {
        throw "包根还有 dll：单文件发布没有生效，先看 WhaleGenie.csproj 的 PublishSingleFile。"
    }

    if (-not (Test-Path (Join-Path $app 'WhaleGenie.exe'))) {
        throw "包里没有 WhaleGenie.exe：单文件发布没有产出程序。"
    }

    # 驱动级输入按文件名找这个库，它必须在 lib 里，不能哪天被当成托管程序集收进 exe。
    if (-not (Test-Path (Join-Path $lib 'libVIIPER.dll'))) {
        throw "lib 里没有 libVIIPER.dll：驱动级输入会在用户那里报 Run.NoDriverLibrary。"
    }

    return $app
}

[System.IO.Directory]::CreateDirectory($OutDir) | Out-Null

foreach ($architecture in $Rid) {
    $published = Join-Path $OutDir "publish-$architecture"
    $package = Join-Path $OutDir "package-$architecture"
    Remove-Folder $published
    Remove-Folder $package

    # 框架依赖和自带运行时各发布一次。单文件发布把托管程序集收进 exe，原生库留在外面，
    # 由 New-PortableFolder 收进 lib；DebugType 关掉托管 pdb，原生 pdb 由项目目标摘掉。
    foreach ($selfContained in @($false, $true)) {
        $target = Join-Path $published $(if ($selfContained) { 'standalone' } else { 'framework' })
        $arguments = @(
            'publish', $project
            '-c', 'Release', '-r', $architecture
            "--self-contained", $selfContained.ToString().ToLowerInvariant()
            '-p:PublishSingleFile=true'
            '-p:IncludeNativeLibrariesForSelfExtract=false'
            '-p:DebugType=none'
            '-p:DebugSymbols=false'
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

    $frameworkZip = Join-Path $OutDir "WhaleGenie-$Tag-$architecture.zip"
    $standaloneZip = Join-Path $OutDir "WhaleGenie-$Tag-$architecture-standalone.zip"
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
