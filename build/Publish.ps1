[CmdletBinding()]
param(
    [string]$DotNetPath = "dotnet",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publicationRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
} elseif ([IO.Path]::IsPathRooted($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
}
$normalizedRepositoryRoot = $repositoryRoot.TrimEnd('\')
$normalizedPublicationRoot = $publicationRoot.TrimEnd('\')
if ($normalizedPublicationRoot -eq $normalizedRepositoryRoot) {
    throw "拒绝使用项目根目录作为发布根目录：$publicationRoot"
}
$filesystemRoot = ([IO.Path]::GetPathRoot($publicationRoot)).TrimEnd('\')
if ($normalizedPublicationRoot -eq $filesystemRoot) {
    throw "拒绝使用文件系统根目录作为发布根目录：$publicationRoot"
}

$artifactsRoot = $publicationRoot
$portableRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "免安装版"))
$installerRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "安装版"))
$applicationVersion = "1.0.0.2"
$applicationDirectoryName = "工程量审减分析助手_" + $applicationVersion.Replace('.', '_')
$applicationDirectory = [IO.Path]::GetFullPath((Join-Path $installerRoot "Application Files\$applicationDirectoryName"))
$projectPath = Join-Path $repositoryRoot "src\EngineeringQuantityAuditComparer.App\EngineeringQuantityAuditComparer.App.csproj"
$portablePublishDir = $portableRoot.TrimEnd('\') + '\'
$clickOncePublishDir = $applicationDirectory.TrimEnd('\') + '\'

function Assert-ArtifactPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $requiredPrefix = $publicationRoot.TrimEnd('\') + '\'
    if ($resolved -eq $publicationRoot -or -not $resolved.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理发布根目录以外的路径：$resolved"
    }
}

foreach ($target in @($portableRoot, $installerRoot)) {
    Assert-ArtifactPath $target
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

Push-Location $repositoryRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_ROLL_FORWARD = "Major"
    & $DotNetPath tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore 失败。" }

    & $DotNetPath publish $projectPath --nologo -p:PublishProfile=Portable "-p:PublishDir=$portablePublishDir" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "免安装版发布失败。" }

    & $DotNetPath publish $projectPath --nologo -p:PublishProfile=ClickOnce "-p:PublishDir=$clickOncePublishDir" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "ClickOnce 应用文件发布失败。" }

    Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.pdb" -Recurse -File | Remove-Item -Force

    & $DotNetPath mage -AddLauncher "工程量审减分析助手.exe" -TargetDirectory $applicationDirectory
    if ($LASTEXITCODE -ne 0) { throw "ClickOnce Launcher 生成失败。" }

    $applicationManifest = Join-Path $applicationDirectory "工程量审减分析助手.exe.manifest"
    & $DotNetPath mage -New Application -ToFile $applicationManifest -Name "工程量审减分析助手" -Version $applicationVersion -Processor msil -FromDirectory $applicationDirectory
    if ($LASTEXITCODE -ne 0) { throw "ClickOnce 应用清单生成失败。" }

    $deploymentManifest = Join-Path $installerRoot "工程量审减分析助手.application"
    & $DotNetPath mage -New Deployment -ToFile $deploymentManifest -Name "工程量审减分析助手" -Version $applicationVersion -Processor msil -Publisher "yang123-pot" -Install true -AppManifest $applicationManifest -AppCodeBase "Application Files\$applicationDirectoryName\工程量审减分析助手.exe.manifest"
    if ($LASTEXITCODE -ne 0) { throw "ClickOnce 部署清单生成失败。" }

    Write-Host "发布完成："
    Write-Host "  免安装版：$portableRoot"
    Write-Host "  ClickOnce：$deploymentManifest"
}
finally {
    Pop-Location
}
