[CmdletBinding()]
param([string]$ExpectedVersion = '1.0.0.3')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$entries = @(Get-ChildItem -LiteralPath $uninstallRoot | ForEach-Object {
    $entry = Get-ItemProperty -LiteralPath $_.PSPath
    if ($entry.DisplayName -like '工程量审减分析助手*') { $entry }
})
$legacy = @($entries | Where-Object { $_.UninstallString -like '*dfshim.dll,ShArpMaintain*' })
if ($legacy.Count -ne 0) { throw "旧 ClickOnce 安装记录仍存在：$($legacy.Count)" }

$current = @($entries | Where-Object { $_.DisplayVersion -eq $ExpectedVersion -and $_.UninstallString -like '*unins*.exe*' })
if ($current.Count -ne 1) { throw "新版安装记录数量异常：$($current.Count)" }
$installLocation = $current[0].InstallLocation
if ([string]::IsNullOrWhiteSpace($installLocation)) { throw '新版安装记录没有安装目录。' }
$exe = Join-Path $installLocation '工程量审减分析助手.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "新版 EXE 不存在：$exe" }

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop '工程量审减分析助手.lnk'
if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { throw "桌面快捷方式不存在：$shortcutPath" }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
if (-not ([IO.Path]::GetFullPath($shortcut.TargetPath).Equals([IO.Path]::GetFullPath($exe), [StringComparison]::OrdinalIgnoreCase))) {
    throw "桌面快捷方式指向错误：$($shortcut.TargetPath)"
}

[pscustomobject]@{
    Version = $current[0].DisplayVersion
    InstallLocation = $installLocation
    DesktopShortcut = $shortcutPath
    ShortcutTarget = $shortcut.TargetPath
    LegacyClickOnceEntries = $legacy.Count
} | ConvertTo-Json -Depth 3
