[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DotNetPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$ExpectedVersion = "1.0.0.1",

    [double]$MaximumInstallerMiB = 80
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishScript = Join-Path $repositoryRoot "build\Publish.ps1"
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)

& $publishScript -DotNetPath $DotNetPath -OutputRoot $resolvedOutputRoot

$versionDirectoryName = "工程量审减分析助手_" + $ExpectedVersion.Replace('.', '_')
$applicationDirectory = Join-Path $resolvedOutputRoot "安装版\Application Files\$versionDirectoryName"
$deploymentManifest = Join-Path $resolvedOutputRoot "安装版\工程量审减分析助手.application"
$portableDirectory = Join-Path $resolvedOutputRoot "免安装版"

if (-not (Test-Path -LiteralPath $applicationDirectory)) {
    throw "未生成预期 ClickOnce 版本目录：$applicationDirectory"
}

$installerFiles = Get-ChildItem -LiteralPath (Join-Path $resolvedOutputRoot "安装版") -Recurse -File
$applicationFiles = Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File
$portableFiles = Get-ChildItem -LiteralPath $portableDirectory -Recurse -File
$installerMiB = [Math]::Round((($installerFiles | Measure-Object Length -Sum).Sum / 1MB), 2)

if ($installerMiB -gt $MaximumInstallerMiB) {
    throw "ClickOnce 安装版体积 $installerMiB MiB 超过上限 $MaximumInstallerMiB MiB。"
}

if ($applicationFiles.Count -gt 15) {
    throw "ClickOnce Application Files 仍有 $($applicationFiles.Count) 个文件，未达到单文件发布预期。"
}

if (@($installerFiles | Where-Object Extension -eq ".pdb").Count -ne 0) {
    throw "ClickOnce 安装版包含 PDB。"
}

if ($portableFiles.Count -gt 15) {
    throw "免安装版文件数应不超过 15，实际为 $($portableFiles.Count)。"
}

$requiredPublishedFiles = @(
    "使用说明.md",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md",
    "THIRD-PARTY-LICENSES\MIT.txt",
    "THIRD-PARTY-LICENSES\Apache-2.0.txt",
    "THIRD-PARTY-LICENSES\System.IO.Packaging-LICENSE.txt",
    "THIRD-PARTY-LICENSES\System.IO.Packaging-THIRD-PARTY-NOTICES.txt",
    "THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-LICENSE.txt",
    "THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.txt",
    "THIRD-PARTY-LICENSES\Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE.txt",
    "Assets\ReportTemplate.xlsx"
)
foreach ($relativePath in $requiredPublishedFiles) {
    foreach ($baseDirectory in @($portableDirectory, $applicationDirectory)) {
        $publishedPath = Join-Path $baseDirectory $relativePath
        if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            throw "发布物缺少：$publishedPath"
        }
    }
}

$sourceTemplate = Join-Path $repositoryRoot "assets\ReportTemplate.xlsx"
$sourceTemplateHash = (Get-FileHash -LiteralPath $sourceTemplate -Algorithm SHA256).Hash
foreach ($publishedTemplate in @(
    (Join-Path $portableDirectory "Assets\ReportTemplate.xlsx"),
    (Join-Path $applicationDirectory "Assets\ReportTemplate.xlsx")
)) {
    if ((Get-FileHash -LiteralPath $publishedTemplate -Algorithm SHA256).Hash -ne $sourceTemplateHash) {
        throw "发布模板与仓库模板不一致：$publishedTemplate"
    }
}

$applicationManifest = Join-Path $applicationDirectory "工程量审减分析助手.exe.manifest"
[xml]$applicationManifestXml = Get-Content -LiteralPath $applicationManifest -Raw
$manifestFiles = @($applicationManifestXml.SelectNodes("//*[local-name()='file']") | ForEach-Object { $_.name })
foreach ($relativePath in $requiredPublishedFiles) {
    if ($relativePath -notin $manifestFiles) {
        throw "ClickOnce 应用清单缺少：$relativePath"
    }
}

[xml]$manifest = Get-Content -LiteralPath $deploymentManifest -Raw
$identity = $manifest.SelectSingleNode("/*[local-name()='assembly']/*[local-name()='assemblyIdentity']")
if ($null -eq $identity -or $identity.version -ne $ExpectedVersion) {
    throw "ClickOnce 部署清单版本不是 $ExpectedVersion。"
}

$result = [pscustomobject]@{
    OutputRoot = $resolvedOutputRoot
    Version = $identity.version
    InstallerFileCount = $installerFiles.Count
    ApplicationFileCount = $applicationFiles.Count
    InstallerMiB = $installerMiB
    PortableFileCount = $portableFiles.Count
    PortableMiB = [Math]::Round((($portableFiles | Measure-Object Length -Sum).Sum / 1MB), 2)
    PdbCount = @($installerFiles | Where-Object Extension -eq ".pdb").Count
}

$result | ConvertTo-Json -Depth 3
