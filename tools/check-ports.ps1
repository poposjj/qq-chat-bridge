param(
    [string]$HostName = "",
    [string]$Ports = "10174 18574 3000 3001 6099"
)

if ([string]::IsNullOrWhiteSpace($HostName)) { $HostName = Read-Host "目标 IP / 域名" }
$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Write-Host ""
Write-Host "============================================"
Write-Host "  目标：$HostName"
Write-Host "  端口：$Ports"
Write-Host "============================================"
Write-Host ""

$open = @()
$closed = @()
foreach ($p in ($Ports -split '\s+')) {
    if ([string]::IsNullOrWhiteSpace($p)) { continue }
    $r = Test-NetConnection -ComputerName $HostName -Port ([int]$p) -WarningAction SilentlyContinue
    if ($r.TcpTestSucceeded) {
        Write-Host "  [开放] $p"
        $open += $p
    } else {
        Write-Host "  [关闭] $p"
        $closed += $p
    }
}

Write-Host ""
Write-Host "  开放：$($open -join ' ')"
Write-Host "  关闭：$($closed -join ' ')"
Write-Host ""
Write-Host "  --------------------------------------------"
Write-Host "  中转端口 18574 的 HTTP 探测"
Write-Host "  --------------------------------------------"

$code = & curl.exe -s -o NUL --noproxy "*" -w "%{http_code}" --max-time 6 "http://$HostName`:18574/api/get_login_info" 2>$null
if ([string]::IsNullOrWhiteSpace($code)) { $code = "000" }

if ($code -eq "000") {
    Write-Host "  [失败] 连不上 18574"
    Write-Host "         1. 云控制台没有把 18574/TCP 映射到服务器"
    Write-Host "         2. 服务器上 relay 没有启动"
} elseif ($code -eq "401") {
    Write-Host "  [正常] relay 在运行（401 = 需要 Token，符合预期）"
    $token = Read-Host "  输入 relay Token 再测一次（直接回车跳过）"
    if (-not [string]::IsNullOrWhiteSpace($token)) {
        $c2 = & curl.exe -s -o NUL --noproxy "*" -w "%{http_code}" --max-time 6 -H "Authorization: Bearer $token" "http://$HostName`:18574/api/get_login_info" 2>$null
        if ([string]::IsNullOrWhiteSpace($c2)) { $c2 = "000" }
        if ($c2 -eq "200") {
            Write-Host "  [成功] Token 正确，relay 到 NapCat 也通"
        } else {
            Write-Host "  [异常] 返回 $c2（401=Token 不对；500/502=NapCat 未登录或没启动）"
        }
    }
} else {
    Write-Host "  [异常] HTTP 返回 $code"
    Write-Host "         200=正常，401=Token 缺失，其它=relay 或 NapCat 有问题"
}

Write-Host ""
Write-Host "  提示："
Write-Host "   - 10174 开放 = RDP，正常"
Write-Host "   - 18574 必须开放，客户端才能连中转"
Write-Host "   - 3000 / 3001 / 6099 不应该对公网开放"
Write-Host ""
