[CmdletBinding()]
param(
    [string]$OutputRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$svg = Join-Path $root 'assets\AppIcon.svg'
$ico = Join-Path $root 'assets\AppIcon.ico'
$wizardImage = Join-Path $root 'assets\InstallerWizard.png'
$smallImage = Join-Path $root 'assets\AppIcon.png'
$iss = Join-Path $root 'build\Installer.iss'
$project = Get-Content -LiteralPath (Join-Path $root 'src\EngineeringQuantityAuditComparer.App\EngineeringQuantityAuditComparer.App.csproj') -Raw
$window = Get-Content -LiteralPath (Join-Path $root 'src\EngineeringQuantityAuditComparer.App\MainWindow.xaml') -Raw
$migrationProject = Join-Path $root 'src\LegacyClickOnceMigration\LegacyClickOnceMigration.csproj'
$releaseNotes = Join-Path $root '更新说明-1.0.0.4.md'

Assert-True (Test-Path -LiteralPath $svg -PathType Leaf) '缺少可编辑的 SVG 图标源文件。'
Assert-True (Test-Path -LiteralPath $ico -PathType Leaf) '缺少 Windows 多尺寸 ICO 图标。'
Assert-True (Test-Path -LiteralPath $wizardImage -PathType Leaf) '缺少安装向导品牌插图。'
Assert-True (Test-Path -LiteralPath $smallImage -PathType Leaf) '缺少安装向导的小图标。'
Assert-True ($project.Contains('<ApplicationIcon>..\..\assets\AppIcon.ico</ApplicationIcon>')) 'EXE 未嵌入应用图标。'
Assert-True ($window.Contains('Icon="/Assets/AppIcon.ico"')) '主窗口未使用应用图标。'

$iconBytes = [IO.File]::ReadAllBytes($ico)
Assert-True ($iconBytes.Length -gt 3000) 'ICO 图标异常小。'
Assert-True ([BitConverter]::ToUInt16($iconBytes, 0) -eq 0 -and [BitConverter]::ToUInt16($iconBytes, 2) -eq 1) '图标不是 ICO 格式。'
$frameCount = [BitConverter]::ToUInt16($iconBytes, 4)
Assert-True ($frameCount -ge 4) "ICO 图标尺寸不足：$frameCount。"
$sizes = @()
for ($index = 0; $index -lt $frameCount; $index++) {
    $width = [int]$iconBytes[6 + 16 * $index]
    $sizes += $(if ($width -eq 0) { 256 } else { $width })
}
foreach ($requiredSize in @(16, 32, 48, 256)) {
    Assert-True ($requiredSize -in $sizes) "ICO 缺少 $requiredSize 像素图层。"
}

Assert-True (Test-Path -LiteralPath $iss -PathType Leaf) '缺少 Inno Setup 安装脚本。'
Assert-True (Test-Path -LiteralPath $migrationProject -PathType Leaf) '缺少旧版自动迁移助手。'
Assert-True (Test-Path -LiteralPath $releaseNotes -PathType Leaf) '缺少本次更新说明。'
Assert-True ($project.Contains('更新说明-1.0.0.4.md')) '更新说明未随安装包发布。'
$installer = Get-Content -LiteralPath $iss -Raw
foreach ($fragment in @(
    'AppId=',
    'PrivilegesRequired=lowest',
    'UsePreviousAppDir=yes',
    'DisableDirPage=no',
    'DisableWelcomePage=yes',
    'DisableReadyPage=yes',
    'DisableFinishedPage=yes',
    'SetupIconFile=',
    'WizardImageFile=',
    'WizardSmallImageFile=',
    'Name: "{autodesktop}\工程量审减分析助手"',
    'function PrepareToInstall',
    'dfshim.dll,ShArpMaintain',
    'RegQueryStringValue',
    'LegacyClickOnceMigration.exe',
    'ExtractTemporaryFile',
    'Exec(',
    'FindLegacyClickOnceEntry'
    'while FindLegacyClickOnceEntry'
    'LegacyDetectionError'
    'RemovePreviousLocation'
    '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
)) {
    Assert-True ($installer.Contains($fragment, [StringComparison]::Ordinal)) "安装脚本缺少：$fragment"
}
Assert-True (-not $installer.Contains("Exec('>', Command", [StringComparison]::Ordinal)) '仍直接展示容易误选的旧版维护窗口。'
Assert-True (-not $installer.Contains('[Run]', [StringComparison]::Ordinal)) '完成页不应再有额外的启动选项。'
Assert-True ($installer.Contains('procedure CurStepChanged', [StringComparison]::Ordinal)) '安装完成后缺少更新提醒。'
Assert-True ($installer.Contains('更新说明-1.0.0.4.md', [StringComparison]::Ordinal)) '完成提醒缺少更新说明入口。'
Assert-True ($installer.Contains('1.0.0.0', [StringComparison]::Ordinal)) '完成提醒未写明从 1.0.0.0 起的累计更新。'
foreach ($summary in @('新点江苏省 V10、V11 清单', '空编号', '审减分析报告', '楼栋')) {
    Assert-True ($installer.Contains($summary, [StringComparison]::Ordinal)) "完成提醒缺少累计更新内容：$summary"
}
$notes = Get-Content -LiteralPath $releaseNotes -Raw
Assert-True ($notes.Contains('新点江苏省 V10、V11 清单', [StringComparison]::Ordinal)) '随包更新说明与弹窗不一致。'
Assert-True ($installer.Contains('WizardForm.SelectDirLabel.Caption', [StringComparison]::Ordinal)) '缺少安装位置选择说明。'
Assert-True ($installer.Contains("Publisher <> '内部使用'", [StringComparison]::Ordinal)) '安装器未识别原内部使用版。'

if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $setup = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) '安装版\工程量审减分析助手-1.0.0.4-安装程序.exe'
    Assert-True (Test-Path -LiteralPath $setup -PathType Leaf) "缺少独立安装程序：$setup"
    Assert-True ((Get-Item -LiteralPath $setup).Length -gt 1MB) '安装程序异常小。'
}

Write-Host '安装程序与应用图标结构检查通过。'
