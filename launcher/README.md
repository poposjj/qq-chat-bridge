# 启动器

Windows 窗体启动器，负责：

- 填机器人 QQ，启动/停止 NapCat
- 登录后从 `get_group_list` 选择要互通的群
- 管理 Token/端口（自动生成、三处同步）
- 管理员设置：接收开关、广播、前缀、发送间隔、模板、多群规则、忽略名单、服务器中转
- 安装模组到 `.minecraft/mods`

## 编译

```bat
build.cmd
```

产物 `QQBot启动器.exe`，需要管理员权限运行（NapCat 注入 QQ 需要）。

## 说明

- 启动器本身是管理员工具，只在本机使用
- 群号/模板/中转地址等设置会整份写入 `.minecraft/config/qqchatbridge.json`
- 中转模式下，模组连接的是 relay，而不是本机 NapCat
