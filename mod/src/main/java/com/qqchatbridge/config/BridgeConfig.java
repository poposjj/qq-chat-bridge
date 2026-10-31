package com.qqchatbridge.config;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * 模组配置，存放在 config/qqchatbridge.json。
 * 所有字段都是 public，Gson 直接读写；改完文件后可用 /qq reload 热重载。
 */
public final class BridgeConfig {

    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
    private static volatile BridgeConfig instance;
    private static volatile long lastModified;

    /* ---------------- 连接 ---------------- */

    /** NapCat 的正向 WebSocket 地址（接收群消息）。 */
    public String wsUrl = "ws://127.0.0.1:3001";
    /** NapCat 的 HTTP API 地址（发送群消息 / 查询群成员）。 */
    public String httpUrl = "http://127.0.0.1:3000";
    /** OneBot 的 access token，没设置就留空。 */
    public String token = "";
    /** 主群号（发送消息默认发到这里）。 */
    public long groupId = 0L;
    /** 多群同步：每个群可以指定哪些玩家能看到（空 = 所有用户）。 */
    public List<GroupEntry> groups = new ArrayList<>();

    public static final class GroupEntry {
        public long id;
        public List<String> viewers = new ArrayList<>();

        public GroupEntry() {
        }

        public GroupEntry(long id) {
            this.id = id;
        }
    }
    /** 机器人自己的 QQ 号，0 = 连上后自动获取。 */
    public long selfId = 0L;

    public boolean autoConnect = true;
    /** 是否把 QQ 群消息显示到游戏聊天栏。 */
    public boolean receive = true;

    /* ---------------- 发送 ---------------- */

    /** 聊天栏里以此前缀开头的消息会转发到 QQ 群，留空则关闭该功能。 */
    public String prefix = ":";
    /** true = 同时把 QQ 消息发到服务器聊天栏（全服可见），false = 只有自己看得到。 */
    public boolean broadcast = false;
    /** 发送节流，两条消息之间的最小间隔（毫秒）。 */
    public int sendIntervalMs = 1200;
    /** 单条消息最大长度，超出会分片发送。 */
    public int maxMessageLength = 900;

    /* ---------------- 显示 ---------------- */

    /** 时间格式，Java DateTimeFormatter 语法。 */
    public String dateFormat = "HH:mm";
    /** 聊天栏里 QQ 消息的前缀。 */
    public String bridgePrefix = "[QQ] ";
    /** 游戏内显示模板，支持 {time} {prefix} {role} {title} {level} {name} {qq} {group} {message}。 */
    public String gameFormat = "&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}";
    /** 发到 QQ 的模板，支持 {player} {message} {time} {world} {group}。 */
    public String qqFormat = "[{player}] {message}";
    /** 服务端下发配置的地址（relay 的 /client-config），留空则不下发。 */
    public String remoteConfigUrl = "";
    /** 拉取下发配置用的 Token，留空则用 token。 */
    public String remoteConfigToken = "";
    /** 显示群主 / 管理员徽章。 */
    /** 显示群内专属头衔（如 [摸鱼王]）。 */
    /** 显示群等级 Lv.x。 */
    /** 在名字后面显示 QQ 号。 */
    /** 鼠标悬停显示昵称/头衔/等级/角色详情。 */
    /** 不显示的 QQ 号列表。 */
    public List<String> ignoreIds = new ArrayList<>();

    private BridgeConfig() {
    }

    public static BridgeConfig get() {
        reloadIfChanged();
        BridgeConfig local = instance;
        if (local == null) {
            synchronized (BridgeConfig.class) {
                if (instance == null) {
                    instance = loadOrCreate();
                }
                local = instance;
            }
        }
        return local;
    }

    private static Path configPath() {
        return FabricLoader.getInstance().getConfigDir().resolve("qqchatbridge.json");
    }

    private static BridgeConfig loadOrCreate() {
        Path path = configPath();
        if (Files.exists(path)) {
            try {
                String json = Files.readString(path, StandardCharsets.UTF_8);
                BridgeConfig loaded = GSON.fromJson(json, BridgeConfig.class);
                if (loaded != null) {
                    lastModified = fileModified();
                    BridgeConfig result = loaded.sanitized();
                    if (result.groupId == 0L && BuildDefaults.DEFAULT_GROUP_ID != 0L) {
                        result.groupId = BuildDefaults.DEFAULT_GROUP_ID;
                        result.groups.clear();
                        result.groups.add(new GroupEntry(result.groupId));
                        result.save();
                    }

                    return result;
                }
            } catch (Exception e) {
                // 配置损坏时备份，避免用户丢失手写内容
                try {
                    Files.move(path, path.resolveSibling("qqchatbridge.json.broken"), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
                } catch (IOException ignored) {
                }
            }
        }
        BridgeConfig created = new BridgeConfig();
        created.applyRelayDefaults();
        created.save();
        return created;
    }

    /** 重新从磁盘加载配置。 */
    public static synchronized BridgeConfig reload() {
        instance = loadOrCreate();
        return instance;
    }

    private static long fileModified() {
        try {
            return Files.getLastModifiedTime(configPath()).toMillis();
        } catch (IOException e) {
            return 0L;
        }
    }

    /** 文件被启动器改写后自动热重载（模板、群列表、广播等立即生效）。 */
    public static synchronized void reloadIfChanged() {
        long modified = fileModified();
        if (modified == 0L) {
            return;
        }
        if (lastModified == 0L) {
            lastModified = modified;
            return;
        }
        if (modified != lastModified) {
            instance = loadOrCreate();
        }
    }

    private BridgeConfig sanitized() {
        if (wsUrl == null) wsUrl = "ws://127.0.0.1:3001";
        if (httpUrl == null) httpUrl = "http://127.0.0.1:3000";
        if (token == null) token = "";
        if (prefix == null) prefix = ":";
        if (dateFormat == null || dateFormat.isBlank()) dateFormat = "HH:mm";
        if (bridgePrefix == null) bridgePrefix = "[QQ] ";
        if (gameFormat == null || gameFormat.isBlank())
        {
            gameFormat = "&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}";
        }
        if (qqFormat == null || qqFormat.isBlank())
        {
            qqFormat = "[{player}] {message}";
        }
        if (ignoreIds == null) ignoreIds = new ArrayList<>();
        if (groups == null) groups = new ArrayList<>();
        if (remoteConfigUrl == null) remoteConfigUrl = "";
        if (remoteConfigToken == null) remoteConfigToken = "";
        for (GroupEntry entry : groups)
        {
            if (entry.viewers == null)
            {
                entry.viewers = new ArrayList<>();
            }
        }
        if (sendIntervalMs < 300) sendIntervalMs = 300;
        if (maxMessageLength < 100) maxMessageLength = 100;
        return this;
    }

    /** 首次启动/旧本地配置迁移：写入构建时内置的中转地址和 Token。 */
    private BridgeConfig applyRelayDefaults() {
        if (BuildDefaults.RELAY_BASE == null || BuildDefaults.RELAY_BASE.isBlank()) {
            return this;
        }
        String base = BuildDefaults.RELAY_BASE.trim();
        wsUrl = "ws://" + base + "/ws";
        httpUrl = "http://" + base + "/api";
        token = BuildDefaults.RELAY_TOKEN;
        remoteConfigUrl = "http://" + base + "/client-config";
        remoteConfigToken = BuildDefaults.RELAY_TOKEN;
        if (groupId == 0L && BuildDefaults.DEFAULT_GROUP_ID != 0L) {
            groupId = BuildDefaults.DEFAULT_GROUP_ID;
            groups.clear();
            groups.add(new GroupEntry(groupId));
        }
        return this;
    }
    /** 用服务端下发的 JSON 覆盖连接/显示/群组等字段（不覆盖 remoteConfig* 本身）。 */
    public static synchronized void applyRemote(JsonObject json) {
        BridgeConfig c = get();
        if (json.has("wsUrl")) c.wsUrl = json.get("wsUrl").getAsString();
        if (json.has("httpUrl")) c.httpUrl = json.get("httpUrl").getAsString();
        if (json.has("token")) c.token = json.get("token").getAsString();
        if (json.has("groupId")) c.groupId = json.get("groupId").getAsLong();
        if (json.has("receive")) c.receive = json.get("receive").getAsBoolean();
        if (json.has("prefix")) c.prefix = json.get("prefix").getAsString();
        if (json.has("broadcast")) c.broadcast = json.get("broadcast").getAsBoolean();
        if (json.has("sendIntervalMs")) c.sendIntervalMs = json.get("sendIntervalMs").getAsInt();
        if (json.has("maxMessageLength")) c.maxMessageLength = json.get("maxMessageLength").getAsInt();
        if (json.has("dateFormat")) c.dateFormat = json.get("dateFormat").getAsString();
        if (json.has("bridgePrefix")) c.bridgePrefix = json.get("bridgePrefix").getAsString();
        if (json.has("gameFormat")) c.gameFormat = json.get("gameFormat").getAsString();
        if (json.has("qqFormat")) c.qqFormat = json.get("qqFormat").getAsString();
        if (json.has("ignoreIds")) {
            c.ignoreIds = new ArrayList<>();
            for (JsonElement e : json.getAsJsonArray("ignoreIds")) {
                c.ignoreIds.add(e.getAsString());
            }
        }
        if (json.has("groups")) {
            c.groups = new ArrayList<>();
            for (JsonElement e : json.getAsJsonArray("groups")) {
                JsonObject g = e.getAsJsonObject();
                GroupEntry entry = new GroupEntry(g.get("id").getAsLong());
                if (g.has("viewers")) {
                    for (JsonElement v : g.getAsJsonArray("viewers")) {
                        entry.viewers.add(v.getAsString());
                    }
                }
                c.groups.add(entry);
            }
        }
        c.sanitized();
    }
    public void save() {
        try {
            Path path = configPath();
            Files.createDirectories(path.getParent());
            Files.writeString(path, GSON.toJson(this), StandardCharsets.UTF_8);
            lastModified = fileModified();
        } catch (IOException ignored) {
        }
    }
}
