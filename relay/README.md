# 中转网关（QQ Relay）

把 NapCat 藏在服务器内网，只暴露一个带 Token 的入口给模组。

## 端点

| 路径 | 说明 |
|---|---|
| `/ws` | WebSocket，转发 NapCat 的 OneBot 事件流 |
| `/api/<action>` | POST，转发 OneBot HTTP API（如 `send_group_msg`） |
| 其它 | 404 |

所有请求都需要：

```
Authorization: Bearer <token>
```

WebSocket 也支持 `?token=<token>`。

## 配置

复制 `relay.ini.example` 为 `relay.ini`，至少改 `token`。

| 字段 | 默认 | 说明 |
|---|---|---|
| listenHost | `+` | 监听地址，`+` 表示所有网卡 |
| listenPort | 18574 | 公网映射端口 |
| napcatWs | ws://127.0.0.1:3001 | NapCat 正向 WS |
| napcatHttp | http://127.0.0.1:3000 | NapCat HTTP |
| token | 必填 | 客户端 Token，≥16 位 |
| allowIps | 空 | 白名单，逗号分隔；空=不限制 |
| maxClients | 64 | 最大并发 WS |
| ratePerMinute | 120 | 每 IP 每分钟请求上限 |
| logFile | relay.log | 日志 |

## 运行

```bat
start-relay.cmd
```

首次需要管理员权限，脚本会自动执行 `netsh http add urlacl`。

## 安全建议

1. 只把 relay 端口映射到公网，NapCat 的 3000/3001 保持本机
2. Token 用 32 位以上随机串，泄露后立即更换（relay.ini + 所有客户端配置）
3. 能固定玩家出口 IP 时，填 `allowIps`
4. 生产环境建议在 relay 前加 TLS 终止（Caddy/Nginx/Cloudflare Tunnel），
   否则 Token 在公网是明文
5. 不要把 relay 放在 80/443/3389 等常见端口上，减少被扫概率
