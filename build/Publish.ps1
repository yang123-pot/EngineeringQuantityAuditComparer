[CmdletBinding()]
param(
    [string]$DotNetPath = "dotnet",
    [string]$OutputRoot = "",
    [string]$InnoCompilerPath = ""
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
$applicationVersion = "1.0.0.3"
$projectPath = Join-Path $repositoryRoot "src\EngineeringQuantityAuditComparer.App\EngineeringQuantityAuditComparer.App.csproj"
$migrationProject = Join-Path $repositoryRoot "src\LegacyClickOnceMigration\LegacyClickOnceMigration.csproj"
$migrationDir = Join-Path $repositoryRoot "src\LegacyClickOnceMigration\bin\Release\net48"
$portablePublishDir = $portableRoot.TrimEnd('\') + '\'
$installerScript = Join-Path $repositoryRoot "build\Installer.iss"

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $compiler) {
        $InnoCompilerPath = $compiler.Source
    } else {
        foreach ($candidate in @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
        )) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $InnoCompilerPath = $candidate
                break
            }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw "缺少 Inno Setup 7 编译器 ISCC.exe。请从 https://jrsoftware.org/isdl.php 安装，或传入 -InnoCompilerPath。"
}

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
    & $DotNetPath publish $projectPath --nologo -p:PublishProfile=Portable "-p:PublishDir=$portablePublishDir" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "免安装版发布失败。" }

    & $DotNetPath build $migrationProject --nologo --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "旧版自动迁移程序编译失败。" }

    Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.pdb" -Recurse -File | Remove-Item -Force

    New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
    & $InnoCompilerPath "/DPublishDir=$portableRoot" "/DMigrationDir=$migrationDir" "/DInstallerOutputDir=$installerRoot" "/DAppVersion=$applicationVersion" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "独立安装程序编译失败。" }

    $setupPath = Join-Path $installerRoot "工程量审减分析助手-$applicationVersion-安装程序.exe"
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw "未生成预期安装程序：$setupPath"
    }

    Write-Host "发布完成："
    Write-Host "  免安装版：$portableRoot"
    Write-Host "  独立安装程序：$setupPath"
}
finally {
    Pop-Location
}
