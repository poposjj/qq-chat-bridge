# QQ Chat Bridge

Minecraft 26.2（Fabric，纯客户端）与 QQ 群双向互通的桥接方案。

包含三部分：

| 目录 | 内容 |
|---|---|
| `mod/` | Minecraft 客户端模组（Java 25 / Fabric） |
| `launcher/` | Windows 图形启动器 + 管理员设置（C# WinForms） |
| `relay/` | 可选的中转网关，把 NapCat 藏在服务器内网（C#） |

## 架构

```
本地模式（默认）
  Minecraft ──ws/http──> 127.0.0.1:3001/3000 (NapCat) ──> QQ

中转模式（服务器下发）
  Minecraft ──ws/http──> relay:18574/ws,/api ──> 127.0.0.1:3001/3000 (NapCat) ──> QQ
```

中转模式下 NapCat 只监听 127.0.0.1，公网只暴露 relay 的一个端口，且必须携带 Token。

## 环境要求

- Minecraft 26.2 + Fabric Loader 0.20.1+ + Java 25
- Windows 10 1809+ / Windows 11 / Windows Server 2019+（QQ NT 限制）
- [NapCat](https://github.com/NapNeko/NapCatQQ)（AGPL-3.0，单独下载，不包含在本仓库）
- QQ NT 9.9.x

## 快速开始（本地模式）

1. 下载 NapCat Shell，解压到启动器同目录的 `napcat\`
2. 编译启动器（见下）或使用 Release
3. 运行 `QQBot启动器.exe`（管理员）
4. 填机器人 QQ → 启动机器人 → 扫码登录
5. 登录后自动弹出“选择群”，选一个群
6. 把 `mod/build/libs/qqchatbridge-1.0.0.jar` 和 Fabric API 放进 `.minecraft/mods`
7. 进游戏，`/qq status`

## 编译

### 模组

```bash
cd mod
./gradlew build          # 需要联网首次下载依赖
# 产物：build/libs/qqchatbridge-1.0.0.jar
```

### 启动器

Windows 自带 .NET Framework 编译器：

```bat
cd launcher
build.cmd
```

或手动：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
  /out:QQBot启动器.exe /win32manifest:app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll Launcher.cs
```

### 中转网关

```bat
cd relay
build.cmd
```

## 中转部署（服务器下发）

1. 把 `relay/` 编译出的 `QQRelay.exe` 和 `relay.ini` 放到 Bot 服务器
2. 修改 `relay.ini`：设置一个足够长的 `token`
3. 启动 `start-relay.cmd`（管理员，首次会自动加 URL ACL）
4. 在云防火墙/安全组把 relay 端口（默认 18574）映射到公网，**不要**映射 NapCat 的 3000/3001
5. 在启动器“管理员设置 → 服务器中转”填入 `公网IP:18574` 和同一个 Token
6. 模组配置会自动变成 `ws://IP:18574/ws`、`http://IP:18574/api`、`token=...`

详见 `docs/部署-中转.md`。

## 安全与隐私

- NapCat 只绑 `127.0.0.1`，公网不可见
- relay 只暴露 `/ws` 和 `/api/<action>`，其它路径一律 404
- 所有请求需要 `Authorization: Bearer <token>`；Token 比较使用固定时间算法
- 每 IP 每分钟请求数限制、最大并发连接数、可选 IP 白名单
- 建议在 relay 前面再加一层 TLS（Caddy / Nginx / Cloudflare Tunnel），避免 Token 明文传输
- 仓库不包含任何 Token、群号、IP、QQ 号等个人数据

## 配置

模组配置：`.minecraft/config/qqchatbridge.json`，字段说明见 `docs/COMMANDS.md`。

启动器会把管理员设置整份写入该文件；模组会按文件修改时间自动热重载（模板/前缀/多群规则等立即生效）。

## 指令

见 `docs/COMMANDS.md`。

## 许可证

MIT（本仓库代码）。NapCat 为 AGPL-3.0，需自行下载并遵守其协议。

## 已知限制

- 中转模式下文字、图片、表情、`/qq say`、多群规则均可用；
  语音 `get_record` 返回的是服务器本地文件，需要额外做媒体代理，暂未内置，
  需要语音时请在 Bot 同机/局域网使用本地模式。
- relay 本身不终结 TLS。公网部署建议前置 Caddy/Nginx/Cloudflare Tunnel，
  客户端把 wsUrl/httpUrl 改成 wss/https。
- 本仓库不含 NapCat、QQ NT、Fabric API、converse、YACL，需按各自协议自行下载。