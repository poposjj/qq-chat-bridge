package com.qqchatbridge.command;

import com.mojang.brigadier.arguments.LongArgumentType;
import com.mojang.brigadier.arguments.StringArgumentType;
import com.qqchatbridge.QqChatBridge;
import com.qqchatbridge.chat.ChatFormat;
import com.qqchatbridge.config.BridgeConfig;
import com.qqchatbridge.media.ClipboardImages;
import com.qqchatbridge.media.ClipboardPreview;
import com.qqchatbridge.qq.OneBotClient;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.command.v2.ClientCommands;
import net.fabricmc.fabric.api.client.command.v2.FabricClientCommandSource;
import net.minecraft.network.chat.Component;

import java.nio.file.Files;
import java.nio.file.Path;

/**
 * 客户端指令 /qq ...（只在本机执行，不会发到服务器）。
 */
public final class QqCommand {

    private QqCommand() {
    }

    public static void register() {
        ClientCommandRegistrationCallback.EVENT.register((dispatcher, access) -> dispatcher.register(
                ClientCommands.literal("qq")
                        .executes(context -> help(context.getSource()))
                        .then(ClientCommands.literal("help")
                                .executes(context -> help(context.getSource())))
                        .then(ClientCommands.literal("enable")
                                .executes(context -> enable(context.getSource())))
                        .then(ClientCommands.literal("disable")
                                .executes(context -> disable(context.getSource())))
                        .then(ClientCommands.literal("status")
                                .executes(context -> status(context.getSource())))
                        .then(ClientCommands.literal("group")
                                .executes(context -> showGroup(context.getSource())))
                        .then(ClientCommands.literal("say")
                                .then(ClientCommands.argument("text", StringArgumentType.greedyString())
                                        .executes(context -> say(context.getSource(),
                                                StringArgumentType.getString(context, "text")))))

                        .then(ClientCommands.literal("face")
                                .then(ClientCommands.argument("id", StringArgumentType.word())
                                        .executes(context -> sendFace(context.getSource(),
                                                StringArgumentType.getString(context, "id")))))
                        .then(ClientCommands.literal("open")
                                .then(ClientCommands.argument("id", StringArgumentType.word())
                                        .executes(context -> openImage(context.getSource(),
                                                StringArgumentType.getString(context, "id")))))
                        .then(ClientCommands.literal("play")
                                .then(ClientCommands.argument("id", StringArgumentType.word())
                                        .executes(context -> playVoice(context.getSource(),
                                                StringArgumentType.getString(context, "id")))))
        ));
    }


    private static void feedback(FabricClientCommandSource source, String text) {
        source.sendFeedback(ChatFormat.parse(text));
    }

    private static int help(FabricClientCommandSource source) {
        feedback(source, "§6===== QQ Chat Bridge =====");
        feedback(source, "§e/qq status §7- 查看连接状态");
        feedback(source, "§e/qq group §7- 查看管理员配置的群（模组内只读，不能改）");
        feedback(source, "§e/qq enable §7/ §e/qq disable §7- 连接 / 断开 NapCat");
        feedback(source, "§e/qq say <内容> §7- 把消息发到 QQ 群");
        feedback(source, "§e/qq face <表情ID> §7- 发送 QQ 表情");
        feedback(source, "§e/qq open <id> §7- 打开收到的图片");
        feedback(source, "§e/qq play <id> §7- 在游戏内播放收到的语音");
        feedback(source, "§7聊天栏里以 §f" + BridgeConfig.get().prefix + " §7开头的消息会直接发到 QQ 群");
        return 1;
    }

    private static int enable(FabricClientCommandSource source) {
        OneBotClient client = QqChatBridge.client();
        if (client == null) {
            feedback(source, "§c[QQ] 客户端尚未初始化");
            return 0;
        }
        client.start();
        feedback(source, "§a[QQ] 正在连接…");
        return 1;
    }

    private static int disable(FabricClientCommandSource source) {
        OneBotClient client = QqChatBridge.client();
        if (client != null) {
            client.stop();
        }
        feedback(source, "§7[QQ] 已断开");
        return 1;
    }

    private static int status(FabricClientCommandSource source) {
        OneBotClient client = QqChatBridge.client();
        BridgeConfig config = BridgeConfig.get();
        boolean connected = client != null && client.isConnected();
        feedback(source, "§6===== QQ Chat Bridge 状态 =====");
        feedback(source, "§7连接：§r" + (connected ? "§a已连接" : "§c未连接"));
        feedback(source, "§7群号：§r" + (config.groupId == 0L ? "§c未设置" : String.valueOf(config.groupId)));
        feedback(source, "§7群名：§r" + (client != null && !client.groupName().isBlank() ? client.groupName() : "§8未知"));
        feedback(source, "§7群限制：§a强制只转发 groupId，其它群丢弃");
        feedback(source, "§7机器人 QQ：§r" + (client != null && client.selfId() != 0L
                ? String.valueOf(client.selfId()) : "§8未知"));
        feedback(source, "§7成员缓存：§r" + (client == null ? 0 : client.memberCacheSize()) + " 人");
        feedback(source, "§7已丢弃其它群消息：§r" + (client == null ? 0 : client.droppedOtherGroups()) + " 条");
        feedback(source, "§7无权限查看而丢弃：§r" + (client == null ? 0 : client.droppedByPermission()) + " 条");
        feedback(source, "§7同步群数：§r" + config.groups.size() + "（主群 " + config.groupId + "）");
        feedback(source, "§7WS 帧：§r" + (client == null ? 0 : client.frames())
                + " §7消息事件：§r" + (client == null ? 0 : client.messageEvents())
                + " §7已显示：§r" + (client == null ? 0 : client.shownMessages()));
        feedback(source, "§7接收开关 receive：§r" + config.receive);
        if (client != null && !client.lastEvent().isEmpty())
        {
            feedback(source, "§8" + client.lastEvent());
        }
        feedback(source, "§7WS：§r" + config.wsUrl);
        feedback(source, "§7HTTP：§r" + config.httpUrl);
        return 1;
    }


    private static int showGroup(FabricClientCommandSource source) {
        BridgeConfig config = BridgeConfig.get();
        if (config.groupId == 0L)
        {
            feedback(source, "§c[QQ] 管理员还没在启动器里配置群号，模组不会连接");
        }
        else
        {
            feedback(source, "§a[QQ] 当前群：§r" + config.groupId + " §7(由启动器配置，模组只读)");
        }
        return 1;
    }

    private static int say(FabricClientCommandSource source, String text) {
        OneBotClient client = QqChatBridge.client();
        if (client == null || !client.isConnected()) {
            feedback(source, "§c[QQ] 尚未连接，先执行 /qq enable");
            return 0;
        }
        String player = net.minecraft.client.Minecraft.getInstance().player != null
                ? net.minecraft.client.Minecraft.getInstance().player.getScoreboardName() : "";
        client.sendGroup(ChatFormat.formatOutbound(player, text));
        ChatFormat.showLocalSent(text);
        return 1;
    }


    private static int sendFace(FabricClientCommandSource source, String id) {
        if (!id.matches("\\d+")) {
            feedback(source, "§c表情 ID 必须是数字，例如 /qq face 14（微笑）");
            return 0;
        }
        OneBotClient client = QqChatBridge.client();
        if (client == null || !client.isConnected()) {
            feedback(source, "§c[QQ] 尚未连接，先执行 /qq enable");
            return 0;
        }
        client.sendFace(id);
        feedback(source, "§a[QQ] 已发送表情 #" + id);
        return 1;
    }

    private static int openImage(FabricClientCommandSource source, String id) {
        OneBotClient client = QqChatBridge.client();
        if (client == null) {
            feedback(source, "§c[QQ] 客户端尚未初始化");
            return 0;
        }
        client.openImage(id);
        feedback(source, "§7[QQ] 正在打开图片…");
        return 1;
    }

    private static int playVoice(FabricClientCommandSource source, String id) {
        OneBotClient client = QqChatBridge.client();
        if (client == null) {
            feedback(source, "§c[QQ] 客户端尚未初始化");
            return 0;
        }
        client.playVoice(id);
        feedback(source, "§7[QQ] 正在获取语音…");
        return 1;
    }
}
