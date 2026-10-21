[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$requiredFiles = @(
    ".gitignore",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md",
    "THIRD-PARTY-LICENSES\MIT.txt",
    "THIRD-PARTY-LICENSES\Apache-2.0.txt",
    "THIRD-PARTY-LICENSES\System.IO.Packaging-LICENSE.txt",
    "THIRD-PARTY-LICENSES\System.IO.Packaging-THIRD-PARTY-NOTICES.txt",
    "THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-LICENSE.txt",
    "THIRD-PARTY-LICENSES\Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.txt",
    "THIRD-PARTY-LICENSES\Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE.txt",
    "assets\ReportTemplate.xlsx"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "缺少开源发布文件：$relativePath"
    Assert-True ((Get-Item -LiteralPath $path).Length -gt 0) "开源发布文件为空：$relativePath"
}

$gitIgnore = Get-Content -LiteralPath (Join-Path $repositoryRoot ".gitignore") -Raw
foreach ($entry in @("outputs/", "artifacts/", ".codex-tmp/", ".superpowers/", "docs/superpowers/", "**/bin/", "**/obj/", "*.xls", "*.xlsx", "*.xlsm", "*.xlsb", "!assets/ReportTemplate.xlsx")) {
    Assert-True ($gitIgnore.Contains($entry, [StringComparison]::Ordinal)) ".gitignore 缺少：$entry"
}

$gitDirectory = Join-Path $repositoryRoot ".git"
if (Test-Path -LiteralPath $gitDirectory) {
    $trackedWorkbooks = @(& git -C $repositoryRoot ls-files -- "*.xls" "*.xlsx" "*.xlsm" "*.xlsb")
    Assert-True ($LASTEXITCODE -eq 0) "无法读取 Git 跟踪文件列表。"
    Assert-True ($trackedWorkbooks.Count -eq 1) "Git 仅允许跟踪一个公开报表模板；实际为：$($trackedWorkbooks -join ', ')"
    Assert-True ($trackedWorkbooks[0].Replace('\', '/') -eq "assets/ReportTemplate.xlsx") "Git 跟踪了未获准公开的工作簿：$($trackedWorkbooks -join ', ')"
}

$license = Get-Content -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Raw
Assert-True ($license.Contains("MIT License", [StringComparison]::Ordinal)) "LICENSE 不是 MIT 许可证。"
Assert-True ($license.Contains("Copyright (c) 2026 yang123-pot", [StringComparison]::Ordinal)) "LICENSE 缺少版权声明。"

$notices = Get-Content -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") -Raw
foreach ($package in @(
    "ClosedXML 0.105.1",
    "ClosedXML.Parser 2.0.0",
    "CommunityToolkit.Mvvm 8.4.2",
    "DocumentFormat.OpenXml 3.1.1",
    "DocumentFormat.OpenXml.Framework 3.1.1",
    "ExcelDataReader 3.9.0",
    "ExcelDataReader.DataSet 3.9.0",
    "ExcelNumberFormat 1.1.0",
    "RBush.Signed 4.0.0",
    "SixLabors.Fonts 1.0.0",
    "System.IO.Packaging 8.0.1",
    "Microsoft.NETCore.App.Runtime.win-x64 10.0.11",
    "Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.11"
)) {
    Assert-True ($notices.Contains($package, [StringComparison]::Ordinal)) "第三方声明缺少：$package"
}
Assert-True ($notices.Contains("THIRD-PARTY-LICENSES/MIT.txt", [StringComparison]::Ordinal)) "第三方声明未链接 MIT 许可证全文。"
Assert-True ($notices.Contains("THIRD-PARTY-LICENSES/Apache-2.0.txt", [StringComparison]::Ordinal)) "第三方声明未链接 Apache-2.0 许可证全文。"

$readme = Get-Content -LiteralPath (Join-Path $repositoryRoot "README.md") -Raw
Assert-True ($readme.Contains("MIT", [StringComparison]::Ordinal)) "README 未声明 MIT 许可证。"
Assert-True ($readme.Contains("开源", [StringComparison]::Ordinal)) "README 未说明开源状态。"

$userGuide = Get-Content -LiteralPath (Join-Path $repositoryRoot "使用说明.md") -Raw
Assert-True ($userGuide.Contains("GitHub", [StringComparison]::Ordinal)) "使用说明未写明开源分发渠道。"
Assert-True (-not $userGuide.Contains("仅从公司内部可信渠道分发", [StringComparison]::Ordinal)) "使用说明仍限制为内部渠道。"

$appProjectPath = Join-Path $repositoryRoot "src\EngineeringQuantityAuditComparer.App\EngineeringQuantityAuditComparer.App.csproj"
$appProject = Get-Content -LiteralPath $appProjectPath -Raw
Assert-True (-not $appProject.Contains("..\..\..\模版.xlsx", [StringComparison]::Ordinal)) "应用项目仍引用仓库外模板。"
Assert-True ($appProject.Contains("..\..\assets\ReportTemplate.xlsx", [StringComparison]::Ordinal)) "应用项目未引用仓库内模板。"
foreach ($publishFile in @("LICENSE", "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-LICENSES")) {
    Assert-True ($appProject.Contains($publishFile, [StringComparison]::Ordinal)) "应用项目未配置发布许可文件：$publishFile"
}

$publicMetadata = $appProject + (Get-Content -LiteralPath (Join-Path $repositoryRoot "build\Publish.ps1") -Raw) + (Get-Content -LiteralPath (Join-Path $repositoryRoot "src\EngineeringQuantityAuditComparer.App\MainWindow.xaml") -Raw)
Assert-True (-not $publicMetadata.Contains("内部使用", [StringComparison]::Ordinal)) "公开版元数据或界面仍包含“内部使用”。"

$testProjectPath = Join-Path $repositoryRoot "tests\EngineeringQuantityAuditComparer.Tests\EngineeringQuantityAuditComparer.Tests.csproj"
$testProject = Get-Content -LiteralPath $testProjectPath -Raw
Assert-True (-not $testProject.Contains("..\..\..\模版.xlsx", [StringComparison]::Ordinal)) "测试项目仍引用仓库外模板。"
Assert-True ($testProject.Contains("..\..\assets\ReportTemplate.xlsx", [StringComparison]::Ordinal)) "测试项目未引用仓库内模板。"
Assert-True (-not [regex]::IsMatch($testProject, "Include=.[^\r\n]*\.\.\\\.\.\\\.\.\\[^\r\n]*\.xls", "IgnoreCase")) "测试项目仍引用仓库外私有 XLS 样例。"

$sourceText = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src"), (Join-Path $repositoryRoot "tests") -Recurse -File -Include *.cs,*.csproj |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw } |
    Out-String
Assert-True (-not [regex]::IsMatch($sourceText, "Sample\(.+\.xls", "IgnoreCase")) "源码或测试仍依赖私有 XLS 样例。"

$templatePath = Join-Path $repositoryRoot "assets\ReportTemplate.xlsx"
Assert-True ((Get-Item -LiteralPath $templatePath).Length -gt 1000) "报表模板文件异常。"

Write-Host "开源包结构检查通过。"
