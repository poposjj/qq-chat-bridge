package com.qqchatbridge.qq;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.qqchatbridge.chat.ChatFormat;
import com.qqchatbridge.config.BridgeConfig;
import com.qqchatbridge.media.MediaStore;
import com.qqchatbridge.media.VoicePlayer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.http.WebSocket;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * 极简 OneBot 11 客户端：
 * <ul>
 *     <li>正向 WebSocket 接收事件（群消息 / 通知）</li>
 *     <li>HTTP API 发送消息、查询群成员</li>
 * </ul>
 * 不依赖任何第三方 QQ 库，避免额外的许可证与嵌套 jar 问题。
 */
public final class OneBotClient {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");

    private final HttpClient http;
    private final ExecutorService io;
    private final ExecutorService sender;
    private final ScheduledExecutorService scheduler;
    private final LinkedBlockingQueue<JsonArray> outbox = new LinkedBlockingQueue<>();
    private final Map<Long, GroupMember> members = new ConcurrentHashMap<>();
    private final java.util.Set<Long> titleQueried = ConcurrentHashMap.newKeySet();

    private volatile boolean running;
    private volatile boolean connected;
    private volatile boolean connecting;
    private volatile WebSocket socket;
    private volatile long selfId;
    private volatile String groupName = "";
    private volatile long lastSendAt;

    public OneBotClient() {
        this.http = HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(8))
                .build();
        this.io = Executors.newFixedThreadPool(2, runnable -> daemon(runnable, "qqchatbridge-io"));
        this.sender = Executors.newSingleThreadExecutor(runnable -> daemon(runnable, "qqchatbridge-send"));
        this.scheduler = Executors.newSingleThreadScheduledExecutor(runnable -> daemon(runnable, "qqchatbridge-reconnect"));
        this.sender.execute(this::sendLoop);
    }

    private static Thread daemon(Runnable runnable, String name) {
        Thread thread = new Thread(runnable, name);
        thread.setDaemon(true);
        return thread;
    }

    /* ---------------- 生命周期 ---------------- */

    public synchronized void start() {
        BridgeConfig config = BridgeConfig.get();
        if (config.groupId == 0L) {
            ChatFormat.notify("§e[QQ] 还没有设置群号，先执行 /qq group <群号>");
            return;
        }
        if (running) {
            return;
        }
        running = true;
        connect();
    }

    public synchronized void stop() {
        running = false;
        connected = false;
        connecting = false;
        WebSocket current = socket;
        socket = null;
        if (current != null) {
            try {
                current.abort();
            } catch (Exception ignored) {
            }
        }
        ChatFormat.notify("§7[QQ] 已断开");
    }

    public synchronized void restart() {
        stop();
        start();
    }

    public boolean isConnected() {
        return connected;
    }

    public long selfId() {
        return selfId;
    }

    public String groupName() {
        return groupName;
    }

    public int memberCacheSize() {
        return members.size();
    }

    public long frames() {
        return frames;
    }

    public long messageEvents() {
        return messageEvents;
    }

    public long shownMessages() {
        return shownMessages;
    }

    public String lastEvent() {
        return lastEvent;
    }

    public long droppedByPermission() {
        return droppedByPermission;
    }

    public long droppedOtherGroups() {
        return droppedOtherGroups;
    }

    /* ---------------- WebSocket ---------------- */

    private void connect() {
        if (!running || connecting) {
            return;
        }
        BridgeConfig config = BridgeConfig.get();
        if (config.wsUrl == null || config.wsUrl.isBlank()) {
            ChatFormat.notify("§c[QQ] wsUrl 为空，无法连接");
            return;
        }
        connecting = true;
        try {
            WebSocket.Builder builder = http.newWebSocketBuilder()
                    .connectTimeout(Duration.ofSeconds(8));
            if (!config.token.isBlank()) {
                builder.header("Authorization", "Bearer " + config.token);
            }
            builder.buildAsync(URI.create(config.wsUrl), new SocketListener())
                    .whenComplete((webSocket, throwable) -> {
                        connecting = false;
                        if (throwable != null) {
                            connected = false;
                            ChatFormat.notify("§c[QQ] 连接失败：" + throwable.getMessage()
                            + "（" + BridgeConfig.get().wsUrl + "）检查中转/NapCat 是否启动、Token 是否一致");
                            scheduleReconnect();
                        }
                    });
        } catch (Exception e) {
            connecting = false;
            ChatFormat.notify("§c[QQ] 连接异常：" + e.getMessage());
            scheduleReconnect();
        }
    }

    private void scheduleReconnect() {
        if (!running) {
            return;
        }
        scheduler.schedule(() -> {
            if (running && !connected) {
                connect();
            }
        }, 5, TimeUnit.SECONDS);
    }

    private final class SocketListener implements WebSocket.Listener {
        private final StringBuilder buffer = new StringBuilder();

        @Override
        public void onOpen(WebSocket webSocket) {
            socket = webSocket;
            connected = true;
            LOGGER.info("OneBot WebSocket 已连接：{}", BridgeConfig.get().wsUrl);
            ChatFormat.notify("§a[QQ] 已连接 OneBot，正在同步群信息…");
            LOGGER.info("[QQ] 已连接 {}，groupId={}，receive={}", BridgeConfig.get().wsUrl,
                    BridgeConfig.get().groupId, BridgeConfig.get().receive);
            io.execute(OneBotClient.this::afterConnect);
            scheduler.schedule(() -> {
                if (connected && frames == 0) {
                    ChatFormat.notify("§e[QQ] 已连接但没收到任何 NapCat 事件：确认 WebSocket 服务（端口 3001）已启用，且没被别的程序占用");
                }
            }, 12, TimeUnit.SECONDS);
            webSocket.request(1);
        }

        @Override
        public CompletionStage<?> onText(WebSocket webSocket, CharSequence data, boolean last) {
            buffer.append(data);
            if (last) {
                String text = buffer.toString();
                buffer.setLength(0);
                frames++;
                if (frames <= 3 || text.contains("\"message\""))
                {
                    LOGGER.info("[QQ] WS 帧 #{}: {}", frames,
                            text.length() > 300 ? text.substring(0, 300) : text);
                }
                try {
                    handle(text);
                } catch (Exception ignored) {
                }
            }
            webSocket.request(1);
            return null;
        }

        @Override
        public CompletionStage<?> onClose(WebSocket webSocket, int statusCode, String reason) {
            connected = false;
            socket = null;
            LOGGER.info("OneBot WebSocket 断开：code={} reason={}", statusCode, reason);
            if (running) {
                ChatFormat.notify("§c[QQ] 连接断开（" + statusCode + "），5 秒后重连");
                scheduleReconnect();
            }
            return null;
        }

        @Override
        public void onError(WebSocket webSocket, Throwable error) {
            connected = false;
            socket = null;
            if (running) {
                scheduleReconnect();
            }
        }
    }

    /* ---------------- 事件处理 ---------------- */

    private void handle(String json) {
        JsonElement parsed = JsonParser.parseString(json);
        if (!parsed.isJsonObject()) {
            return;
        }
        JsonObject event = parsed.getAsJsonObject();
        String postType = MessageText.optString(event, "post_type");
        if (postType.isEmpty() && event.has("message_type")) {
            postType = "message";
        }
        String snippet = json.length() > 220 ? json.substring(0, 220) : json;
        lastEvent = snippet;
        if (event.has("self_id")) {
            selfId = event.get("self_id").getAsLong();
        }
        switch (postType) {
            case "message" -> handleMessage(event, false);
            case "message_sent" -> handleMessage(event, true);
            case "notice" -> handleNotice(event);
            default -> {
                // meta_event（心跳/生命周期）忽略
            }
        }
    }

    private void handleMessage(JsonObject event, boolean selfSent) {
        if (!"group".equals(MessageText.optString(event, "message_type"))) {
            return;
        }
        BridgeConfig config = BridgeConfig.get();
        messageEvents++;
        long groupId = event.has("group_id") ? event.get("group_id").getAsLong() : 0L;
        if (config.groupId == 0L && (config.groups == null || config.groups.isEmpty())) {
            notifyGroupNotBound();
            return;
        }
        BridgeConfig.GroupEntry rule = findRule(config, groupId);
        if (config.groups != null && !config.groups.isEmpty()) {
            if (rule == null) {
                droppedOtherGroups++;
                return;
            }
        } else if (groupId != config.groupId) {
            droppedOtherGroups++;
            return;
        }
        if (rule != null && !canView(rule)) {
            droppedByPermission++;
            return;
        }
        long userId = event.has("user_id") ? event.get("user_id").getAsLong() : 0L;
        if (!selfSent && (userId == 0L || userId == selfId)) {
            return;
        }
        if (config.ignoreIds.contains(String.valueOf(userId))) {
            return;
        }

        JsonObject sender = event.has("sender") && event.get("sender").isJsonObject()
                ? event.getAsJsonObject("sender")
                : new JsonObject();
        GroupMember fresh = new GroupMember(userId,
                MessageText.optString(sender, "nickname"),
                MessageText.optString(sender, "card"),
                MessageText.optString(sender, "role"),
                MessageText.optString(sender, "level"),
                MessageText.optString(sender, "title"));
        GroupMember cached = members.get(userId);
        GroupMember member = merge(cached, fresh);
        members.put(userId, member);
        if (member.title().isBlank() && titleQueried.add(userId)) {
            fetchMember(userId);
        }

        List<Segment> parsed = MessageText.parse(event);
        if (parsed.isEmpty()) {
            return;
        }
        List<Segment> display = new ArrayList<>();
        StringBuilder plain = new StringBuilder();
        for (Segment segment : parsed) {
            switch (segment.type()) {
                case IMAGE -> {
                    String mediaId = MediaStore.registerImage(segment.fileId(), segment.url());
                    display.add(segment.withMediaId(mediaId));
                    plain.append("[图片]");
                }
                case VOICE -> {
                    String mediaId = MediaStore.registerVoice(segment.fileId(), segment.url());
                    display.add(segment.withMediaId(mediaId));
                    plain.append("[语音]");
                }
                case FACE -> {
                    display.add(segment);
                    plain.append('[').append(segment.text()).append(']');
                }
                case TEXT -> {
                    display.add(segment);
                    plain.append(segment.text());
                }
            }
        }
        if (selfSent && wasRecentlySent(plain.toString())) {
            return;
        }
        LOGGER.debug("[QQ] {} ({}): {}", member.displayName(), userId, plain);
        if (config.receive) {
            String groupTag = (config.groups != null && config.groups.size() > 1)
                    ? ("[" + groupId + "] ") : "";
            ChatFormat.showGroupMessage(member, display, groupTag);
            shownMessages++;
        }
    }

    private static BridgeConfig.GroupEntry findRule(BridgeConfig config, long groupId) {
        if (config.groups == null)
        {
            return null;
        }
        for (BridgeConfig.GroupEntry entry : config.groups)
        {
            if (entry.id == groupId)
            {
                return entry;
            }
        }
        return null;
    }

    private static boolean canView(BridgeConfig.GroupEntry rule) {
        if (com.qqchatbridge.Edition.isAdmin())
        {
            return true;
        }
        if (rule.viewers == null || rule.viewers.isEmpty())
        {
            return true;
        }
        String name = playerName();
        if (name == null)
        {
            return false;
        }
        for (String viewer : rule.viewers)
        {
            if (viewer != null && viewer.equalsIgnoreCase(name))
            {
                return true;
            }
        }
        return false;
    }

    private static String playerName() {
        try
        {
            net.minecraft.client.Minecraft minecraft = net.minecraft.client.Minecraft.getInstance();
            if (minecraft == null || minecraft.player == null)
            {
                return null;
            }
            return minecraft.player.getScoreboardName();
        }
        catch (Exception e)
        {
            return null;
        }
    }

    private boolean wasRecentlySent(String text) {
        Long at = recentOutbound.remove(text);
        return at != null && System.currentTimeMillis() - at < 15000L;
    }

    private static GroupMember merge(GroupMember cached, GroupMember fresh) {
        if (cached == null) {
            return fresh;
        }
        return new GroupMember(
                fresh.userId(),
                fresh.nickname().isBlank() ? cached.nickname() : fresh.nickname(),
                fresh.card().isBlank() ? cached.card() : fresh.card(),
                fresh.role().isBlank() ? cached.role() : fresh.role(),
                fresh.level().isBlank() ? cached.level() : fresh.level(),
                fresh.title().isBlank() ? cached.title() : fresh.title());
    }

    private boolean warnedUnbound;
    private long frames;
    private long messageEvents;
    private long shownMessages;
    private volatile String lastEvent = "";
    private final java.util.Map<String, Long> recentOutbound = new java.util.concurrent.ConcurrentHashMap<>();
    private volatile long droppedOtherGroups;
    private volatile long droppedByPermission;

    private void notifyGroupNotBound() {
        if (warnedUnbound) {
            return;
        }
        warnedUnbound = true;
        ChatFormat.notify("§e[QQ] 管理员还没在启动器里配置 QQ 群号，请联系管理员填写后重进游戏");
    }

    private void handleNotice(JsonObject event) {
        BridgeConfig config = BridgeConfig.get();
        long noticeGroup = event.has("group_id") ? event.get("group_id").getAsLong() : 0L;
        if (config.groupId == 0L || noticeGroup != config.groupId) {
            return;
        }
        String noticeType = MessageText.optString(event, "notice_type");
        long userId = event.has("user_id") ? event.get("user_id").getAsLong() : 0L;
        if (userId == 0L) {
            return;
        }
        if ("group_card".equals(noticeType)) {
            GroupMember old = members.get(userId);
            if (old != null) {
                members.put(userId, old.withCard(MessageText.optString(event, "card_new")));
            }
        } else if ("group_title".equals(noticeType)) {
            GroupMember old = members.get(userId);
            if (old != null) {
                members.put(userId, old.withTitle(MessageText.optString(event, "title")));
            }
        }
    }

    private void afterConnect() {
        BridgeConfig config = BridgeConfig.get();

        JsonObject login = post("get_login_info", new JsonObject());
        if (login != null && login.has("data") && login.get("data").isJsonObject()) {
            JsonObject data = login.getAsJsonObject("data");
            if (data.has("user_id")) {
                selfId = data.get("user_id").getAsLong();
            }
        }

        JsonObject groupInfoParams = new JsonObject();
        groupInfoParams.addProperty("group_id", config.groupId);
        groupInfoParams.addProperty("no_cache", false);
        JsonObject groupInfo = post("get_group_info", groupInfoParams);
        if (groupInfo != null && groupInfo.has("data") && groupInfo.get("data").isJsonObject()) {
            JsonObject data = groupInfo.getAsJsonObject("data");
            if (data.has("group_name")) {
                groupName = data.get("group_name").getAsString();
            }
        }

        prefetchMembers();
        ChatFormat.notify("§a[QQ] 已连接群聊：" + (groupName.isBlank() ? String.valueOf(config.groupId) : groupName)
                + "（成员缓存 " + members.size() + " 人）");
    }

    /* ---------------- 群成员 ---------------- */

    private void prefetchMembers() {
        JsonObject params = new JsonObject();
        params.addProperty("group_id", BridgeConfig.get().groupId);
        JsonObject response = post("get_group_member_list", params);
        if (response == null) {
            return;
        }
        JsonElement data = response.get("data");
        if (data == null || !data.isJsonArray()) {
            return;
        }
        for (JsonElement element : data.getAsJsonArray()) {
            if (!element.isJsonObject()) {
                continue;
            }
            JsonObject object = element.getAsJsonObject();
            if (!object.has("user_id")) {
                continue;
            }
            long userId = object.get("user_id").getAsLong();
            members.put(userId, new GroupMember(userId,
                    MessageText.optString(object, "nickname"),
                    MessageText.optString(object, "card"),
                    MessageText.optString(object, "role"),
                    MessageText.optString(object, "level"),
                    MessageText.optString(object, "title")));
            titleQueried.add(userId);
        }
    }

    private void fetchMember(long userId) {
        io.execute(() -> {
            JsonObject params = new JsonObject();
            params.addProperty("group_id", BridgeConfig.get().groupId);
            params.addProperty("user_id", userId);
            params.addProperty("no_cache", false);
            JsonObject response = post("get_group_member_info", params);
            if (response == null || !response.has("data") || !response.get("data").isJsonObject()) {
                return;
            }
            JsonObject data = response.getAsJsonObject("data");
            GroupMember old = members.get(userId);
            members.put(userId, new GroupMember(userId,
                    MessageText.optString(data, "nickname"),
                    MessageText.optString(data, "card"),
                    MessageText.optString(data, "role"),
                    MessageText.optString(data, "level"),
                    MessageText.optString(data, "title")));
            if (old == null) {
                return;
            }
        });
    }

    /* ---------------- 发送 ---------------- */

    public void sendGroup(String text) {
        if (text == null) {
            return;
        }
        String trimmed = text.trim();
        if (trimmed.isEmpty()) {
            return;
        }
        if (!connected) {
            ChatFormat.notify("§c[QQ] 未连接，消息没有发出去");
            return;
        }
        recentOutbound.put(trimmed, System.currentTimeMillis());
        for (String chunk : chunk(trimmed, BridgeConfig.get().maxMessageLength)) {
            outbox.offer(buildMessage(FaceTable.split(chunk)));
        }
    }

    /** 发送本地图片（base64 上传，NapCat 会把它转成图片消息）。 */
    public void sendImage(Path path) {
        sendImages(List.of(path));
    }

    /** 一次发送多张图片：放进同一条消息的多个 image 段，总大小限制 8MB。 */
    public void sendImages(List<Path> paths) {
        if (!connected) {
            ChatFormat.notify("§c[QQ] 未连接，图片没有发出去");
            return;
        }
        JsonArray array = new JsonArray();
        long total = 0L;
        for (Path path : paths) {
            try {
                long size = Files.size(path);
                if (size <= 0) {
                    continue;
                }
                if (total + size > 8L * 1024 * 1024) {
                    ChatFormat.notify("§e[QQ] 图片总大小超过 8MB，后面的没有发送");
                    break;
                }
                total += size;
                byte[] bytes = Files.readAllBytes(path);
                JsonObject data = new JsonObject();
                data.addProperty("file", "base64://" + Base64.getEncoder().encodeToString(bytes));
                JsonObject object = new JsonObject();
                object.addProperty("type", "image");
                object.add("data", data);
                array.add(object);
            } catch (Exception e) {
                ChatFormat.notify("§c[QQ] 读取图片失败：" + path.getFileName());
            }
        }
        if (array.size() == 0) {
            ChatFormat.notify("§c[QQ] 没有可发送的图片");
            return;
        }
        outbox.offer(array);
        ChatFormat.notify("§a[QQ] 正在发送 " + array.size() + " 张图片");
    }

    /** 发送 QQ 表情（face id 见 /qq faces 或 faces.json）。 */
    public void sendFace(String id) {
        if (!connected) {
            ChatFormat.notify("§c[QQ] 未连接，表情没有发出去");
            return;
        }
        JsonObject data = new JsonObject();
        data.addProperty("id", id);
        JsonObject object = new JsonObject();
        object.addProperty("type", "face");
        object.add("data", data);
        JsonArray array = new JsonArray();
        array.add(object);
        outbox.offer(array);
    }

    /** 点击聊天栏里的 [语音]：取语音并转 wav，在游戏内播放。 */
    public void playVoice(String mediaId) {
        MediaStore.Entry entry = MediaStore.get(mediaId);
        if (entry == null || entry.kind() != MediaStore.Kind.VOICE) {
            ChatFormat.notify("§c[QQ] 语音已过期，让对面重发一条吧");
            return;
        }
        io.execute(() -> {
            try {
                Path wav = MediaStore.directory().resolve(mediaId + ".wav");
                if (!Files.exists(wav) || Files.size(wav) == 0) {
                    JsonObject params = new JsonObject();
                    params.addProperty("file", entry.fileId());
                    params.addProperty("out_format", "wav");
                    JsonObject response = post("get_record", params);
                    JsonObject data = response != null && response.has("data") && response.get("data").isJsonObject()
                            ? response.getAsJsonObject("data")
                            : null;
                    if (data == null) {
                        ChatFormat.notify("§c[QQ] 获取语音失败（NapCat 可能不支持 wav 转换）");
                        return;
                    }
                    wav = MediaStore.storeVoice(mediaId, MessageText.optString(data, "file"));
                }
                VoicePlayer.play(wav);
            } catch (Exception e) {
                ChatFormat.notify("§c[QQ] 播放语音失败：" + e.getMessage());
            }
        });
    }

    /** 点击聊天栏里的 [图片]：下载后用系统看图工具打开。 */
    public void openImage(String mediaId) {
        MediaStore.Entry entry = MediaStore.get(mediaId);
        if (entry == null || entry.kind() != MediaStore.Kind.IMAGE) {
            ChatFormat.notify("§c[QQ] 图片已过期");
            return;
        }
        io.execute(() -> {
            try {
                Path image = MediaStore.ensureImage(mediaId);
                MediaStore.open(image);
            } catch (Exception e) {
                ChatFormat.notify("§c[QQ] 打开图片失败：" + e.getMessage());
            }
        });
    }

    private JsonArray buildMessage(List<Segment> segments) {
        JsonArray array = new JsonArray();
        for (Segment segment : segments) {
            JsonObject object = new JsonObject();
            JsonObject data = new JsonObject();
            if (segment.type() == Segment.Type.TEXT) {
                object.addProperty("type", "text");
                data.addProperty("text", segment.text());
            } else if (segment.type() == Segment.Type.FACE) {
                object.addProperty("type", "face");
                data.addProperty("id", segment.fileId());
            } else {
                continue;
            }
            object.add("data", data);
            array.add(object);
        }
        return array;
    }

    private static List<String> chunk(String text, int max) {
        List<String> chunks = new ArrayList<>();
        if (max <= 0) {
            chunks.add(text);
            return chunks;
        }
        int start = 0;
        while (start < text.length()) {
            int end = Math.min(text.length(), start + max);
            // 不要把 emoji 的代理对从中间劈开
            if (end < text.length() && Character.isHighSurrogate(text.charAt(end - 1))) {
                end--;
            }
            if (end <= start) {
                end = Math.min(text.length(), start + max);
            }
            chunks.add(text.substring(start, end));
            start = end;
        }
        return chunks;
    }

    private void sendLoop() {
        while (true) {
            JsonArray message;
            try {
                message = outbox.take();
            } catch (InterruptedException e) {
                return;
            }
            try {
                if (BridgeConfig.get().groupId == 0L) {
                    ChatFormat.notify("§c[QQ] 没有群号，消息未发送");
                    continue;
                }
                long wait = BridgeConfig.get().sendIntervalMs - (System.currentTimeMillis() - lastSendAt);
                if (wait > 0) {
                    Thread.sleep(wait);
                }
                JsonObject response = post("send_group_msg", buildSendParams(message));
                lastSendAt = System.currentTimeMillis();
                if (response == null) {
                    ChatFormat.notify("§c[QQ] 发送失败：HTTP 请求失败");
                } else if (response.has("retcode") && response.get("retcode").getAsInt() != 0) {
                    ChatFormat.notify("§c[QQ] 发送失败：retcode=" + response.get("retcode").getAsInt()
                            + "（可能是限流或风控）");
                }
            } catch (InterruptedException e) {
                return;
            } catch (Exception e) {
                ChatFormat.notify("§c[QQ] 发送异常：" + e.getMessage());
            }
        }
    }

    private JsonObject buildSendParams(JsonArray message) {
        JsonObject params = new JsonObject();
        params.addProperty("group_id", BridgeConfig.get().groupId);
        params.add("message", message);
        return params;
    }

    /* ---------------- HTTP ---------------- */

    private JsonObject post(String action, JsonObject params) {
        BridgeConfig config = BridgeConfig.get();
        String base = config.httpUrl.endsWith("/")
                ? config.httpUrl.substring(0, config.httpUrl.length() - 1)
                : config.httpUrl;
        try {
            HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(base + "/" + action))
                    .timeout(Duration.ofSeconds(10))
                    .header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(params.toString(), StandardCharsets.UTF_8));
            if (!config.token.isBlank()) {
                builder.header("Authorization", "Bearer " + config.token);
            }
            HttpResponse<String> response = http.send(builder.build(),
                    HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
            String body = response.body();
            if (body == null || body.isBlank()) {
                return null;
            }
            JsonElement element = JsonParser.parseString(body);
            return element.isJsonObject() ? element.getAsJsonObject() : null;
        } catch (Exception e) {
            return null;
        }
    }
}
