# 指令与格式

## 两个模组版本

| jar | 说明 |
|---|---|
| `qqchatbridge-1.0.0.jar` | 用户版：只有用户指令 |
| `qqchatbridge-admin-1.0.0.jar` | 管理员版：额外包含管理员指令，忽略“可见玩家”限制 |

二选一安装。

## 用户版指令

| 指令 | 作用 |
|---|---|
| `/qq` `/qq help` | 帮助 |
| `/qq status` | 连接、群号、帧数、丢弃计数 |
| `/qq group` | 查看绑定群（只读） |
| `/qq enable` / `/qq disable` | 连接 / 断开 |
| `/qq say <内容>` | 发文字到 QQ 主群（走“游戏→QQ”模板） |
| `/qq face <id>` | 发送 QQ 表情 |
| `/qq open <id>` | 打开收到的图片 |
| `/qq play <id>` | 播放收到的语音 |
| 聊天栏 `:内容` | 发到 QQ 主群 |
| `Ctrl+V` + `Enter` | 粘贴图片，回车发到 QQ（无需指令） |

## 管理员版额外指令

| 指令 | 作用 |
|---|---|
| `/qq reload` | 重新读取配置并重连 |
| `/qq broadcast <true\|false>` | 开关全服广播 |
| `/qq group <群号>` | 切换发送主群 |
| `/qq group off` | 清除主群并断开 |

## 游戏内显示模板（QQ → 游戏）

默认：

```
&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}
```

| 占位符 | 含义 |
|---|---|
| `{time}` | 时间（dateFormat） |
| `{prefix}` | bridgePrefix |
| `{role}` | 群主/管理员徽章 |
| `{title}` | 群头衔 |
| `{level}` | 群等级 |
| `{name}` | 昵称/群名片（带悬停） |
| `{qq}` | QQ 号 |
| `{group}` | 多群标签 |
| `{message}` | 正文 |

颜色：`&0`~`&f`、`&l` `&o` `&n` `&m` `&r`，也支持 `§`。

## 发到 QQ 的模板（游戏 → QQ）

默认：

```
[{player}] {message}
```

示例：

```
Hypixel: [{player}] {message}
[{player}] 说：{message}（{world}）
Привет, {player}: {message}
```

占位符：`{player}` `{message}` `{time}` `{world}` `{group}`。

模板里的其它文字原样输出，支持任意 Unicode；占位符只替换一次，
玩家消息里出现 `{time}` 不会被二次展开。

生效范围：聊天栏 `:` 前缀、`/qq say`。图片/表情走单独接口。

启动器保存设置后，模组按文件修改时间自动热重载，不用重启游戏。

## 多群同步与可见玩家

启动器 → 管理员设置 → 多群同步，每行一条：

```
群号=*
群号=玩家1,玩家2
```

- `*` 或留空：所有用户可见
- 玩家名大小写不敏感
- 不在列表里的群不转发
- 列表为空：只同步主群，所有人可见
- 管理员版忽略可见玩家限制

## 模组配置字段

`config/qqchatbridge.json`：

| 字段 | 说明 |
|---|---|
| wsUrl / httpUrl | NapCat 或 relay 地址 |
| token | OneBot Token 或 relay Token |
| groupId | 发送主群 |
| groups | 多群规则 `[{id, viewers[]}]` |
| receive | 是否显示群消息 |
| prefix | 聊天栏触发前缀 |
| broadcast | QQ 消息是否发到服务器聊天 |
| sendIntervalMs / maxMessageLength | 发送节流与分片 |
| gameFormat / qqFormat | 两个模板 |
| ignoreIds | 忽略的 QQ 号 |
| remoteConfigUrl | relay 的 /client-config 地址，留空不下发 |
| remoteConfigToken | 拉取下发配置的 Token，留空用 token |
| dateFormat / bridgePrefix | 时间格式与前缀 |
