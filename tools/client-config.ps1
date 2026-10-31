param(
    [string]$RelayHost = "",
    [string]$Token = "",
    [string]$GroupId = "",
    [string]$GameDir = "",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Write-Host ""
Write-Host "============================================"
Write-Host "  生成 QQ Chat Bridge 客户端配置（中转模式）"
Write-Host "============================================"
Write-Host ""

if ([string]::IsNullOrWhiteSpace($RelayHost)) {
    $RelayHost = Read-Host "中转地址（公网IP:外部端口，例如 103.236.99.243:28574）"
}
$RelayHost = $RelayHost.Trim()
if ($RelayHost -match '^https?://') { $RelayHost = $RelayHost -replace '^https?://','' }
$RelayHost = $RelayHost.TrimEnd('/')
if ($RelayHost -notmatch ':') {
    Write-Host "地址必须带端口，例如 1.2.3.4:28574" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = Read-Host "relay Token（启动器管理员设置里那个）"
}
$Token = $Token.Trim()
if ($Token.Length -lt 16) {
    Write-Host "Token 太短，至少 16 位" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($GroupId)) {
    $GroupId = Read-Host "主群号（纯数字）"
}
$GroupId = $GroupId.Trim()
if ($GroupId -notmatch '^\d+$') {
    Write-Host "群号必须是数字" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $candidate = Join-Path $env:APPDATA ".minecraft"
    if (Test-Path $candidate) {
        $GameDir = $candidate
    } else {
        $GameDir = Read-Host "找不到 %APPDATA%\.minecraft，请手动输入 .minecraft 路径"
    }
}
$GameDir = $GameDir.Trim().Trim('"')
if (-not (Test-Path $GameDir)) {
    Write-Host "目录不存在：$GameDir" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

$configDir = Join-Path $GameDir "config"
if (-not (Test-Path $configDir)) { New-Item -ItemType Directory -Path $configDir | Out-Null }
$path = Join-Path $configDir "qqchatbridge.json"
if (Test-Path $path) {
    Copy-Item $path ($path + ".bak") -Force
    Write-Host "已备份旧配置：$path.bak"
}

$escapedToken = $Token.Replace('\','\\').Replace('"','\"')
$json = @"
{
  "wsUrl": "ws://$RelayHost/ws",
  "httpUrl": "http://$RelayHost/api",
  "token": "$escapedToken",
  "remoteConfigUrl": "http://$RelayHost/client-config",
  "remoteConfigToken": "$escapedToken",
  "groupId": $GroupId,
  "selfId": 0,
  "autoConnect": true,
  "receive": true,
  "prefix": ":",
  "broadcast": false,
  "sendIntervalMs": 1200,
  "maxMessageLength": 900,
  "dateFormat": "HH:mm",
  "bridgePrefix": "[QQ] ",
  "gameFormat": "&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}",
  "qqFormat": "[{player}] {message}",
  "groups": [
    { "id": $GroupId, "viewers": [] }
  ],
  "ignoreIds": []
}
"@

[IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding($false)))
Write-Host ""
Write-Host "已写入：$path" -ForegroundColor Green
Write-Host "wsUrl  : ws://$RelayHost/ws"
Write-Host "httpUrl: http://$RelayHost/api"
Write-Host "群号   : $GroupId"
Write-Host ""
Write-Host "重开游戏后执行 /qq status 检查连接。"
if (-not $NoPause) { Read-Host "按回车退出" }
