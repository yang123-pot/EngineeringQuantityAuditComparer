[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$DotNetPath,
    [Parameter(Mandatory = $true)] [string]$OutputRoot,
    [string]$InnoCompilerPath = "",
    [string]$ExpectedVersion = "1.0.0.3",
    [double]$MaximumInstallerMiB = 80
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [IO.Path]::GetFullPath($OutputRoot)

& (Join-Path $root "build\Publish.ps1") -DotNetPath $DotNetPath -OutputRoot $output -InnoCompilerPath $InnoCompilerPath

$installerRoot = Join-Path $output "安装版"
$portableRoot = Join-Path $output "免安装版"
$setup = Join-Path $installerRoot "工程量审减分析助手-$ExpectedVersion-安装程序.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw "未生成预期独立安装程序：$setup"
}
if (@(Get-ChildItem -LiteralPath $installerRoot -File).Count -ne 1) {
    throw "安装版应只包含一个可运行的安装程序。"
}
if (@(Get-ChildItem -LiteralPath $installerRoot -Recurse -File | Where-Object Extension -eq '.pdb').Count -ne 0) {
    throw "安装版包含 PDB。"
}

$installerMiB = [Math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 2)
if ($installerMiB -gt $MaximumInstallerMiB) {
    throw "独立安装程序体积 $installerMiB MiB 超过上限 $MaximumInstallerMiB MiB。"
}

$portableFiles = @(Get-ChildItem -LiteralPath $portableRoot -Recurse -File)
if ($portableFiles.Count -gt 15) {
    throw "免安装版文件数应不超过 15，实际为 $($portableFiles.Count)。"
}

$required = @(
    '工程量审减分析助手.exe',
    '使用说明.md',
    '更新说明-1.0.0.3.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'THIRD-PARTY-LICENSES\MIT.txt',
    'THIRD-PARTY-LICENSES\Apache-2.0.txt',
    'THIRD-PARTY-LICENSES\System.IO.Packaging-LICENSE.txt',
    'THIRD-PARTY-LICENSES\System.IO.Packaging-THIRD-PARTY-NOTICES.txt',
    'THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-LICENSE.txt',
    'THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.txt',
    'THIRD-PARTY-LICENSES\Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE.txt',
    'Assets\ReportTemplate.xlsx'
)
foreach ($relative in $required) {
    $path = Join-Path $portableRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "发布物缺少：$path"
    }
}

$sourceTemplate = Join-Path $root 'assets\ReportTemplate.xlsx'
$publishedTemplate = Join-Path $portableRoot 'Assets\ReportTemplate.xlsx'
if ((Get-FileHash -LiteralPath $sourceTemplate -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $publishedTemplate -Algorithm SHA256).Hash) {
    throw "发布模板与仓库模板不一致。"
}

$exe = Join-Path $portableRoot '工程量审减分析助手.exe'
$fileVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if (-not $fileVersion.StartsWith($ExpectedVersion, [StringComparison]::Ordinal)) {
    throw "EXE 版本异常：$fileVersion"
}

& (Join-Path $root 'tests\Installer.Tests.ps1') -OutputRoot $output
if ($LASTEXITCODE -ne 0) { throw '安装结构检查失败。' }

[pscustomobject]@{
    OutputRoot = $output
    Version = $ExpectedVersion
    InstallerMiB = $installerMiB
    PortableFileCount = $portableFiles.Count
    PortableMiB = [Math]::Round((($portableFiles | Measure-Object Length -Sum).Sum / 1MB), 2)
} | ConvertTo-Json -Depth 3
