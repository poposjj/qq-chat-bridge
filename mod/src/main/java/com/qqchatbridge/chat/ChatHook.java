package com.qqchatbridge.chat;

import com.qqchatbridge.QqChatBridge;
import com.qqchatbridge.config.BridgeConfig;
import com.qqchatbridge.media.ClipboardImages;
import com.qqchatbridge.media.ClipboardPreview;
import com.qqchatbridge.qq.OneBotClient;import net.fabricmc.fabric.api.client.message.v1.ClientSendMessageEvents;

/**
 * 拦截聊天栏发送：
 * <ul>
 *     <li>以配置前缀（默认 ":"）开头的消息改发 QQ 群，不再发到服务器；</li>
 *     <li>直接粘图片路径后回车会被拦下，提示用 /qq image 发送。</li>
 * </ul>
 */
public final class ChatHook {

    private ChatHook() {
    }

    public static void register() {
        ClientSendMessageEvents.ALLOW_CHAT.register(ChatHook::allowChat);
    }

    private static boolean allowChat(String message) {
        if (message == null || message.isBlank()) {
            return true;
        }
        if (message.contains("[QQ图片]")) {
            java.util.List<java.nio.file.Path> pending = ClipboardImages.pending();
            if (pending.isEmpty()) {
                ChatFormat.notify("§e[QQ] 输入框里有 [QQ图片] 但没有图片，请先 Ctrl+V 粘贴");
                return false;
            }
            OneBotClient client = QqChatBridge.client();
            if (client == null || !client.isConnected()) {
                ChatFormat.notify("§c[QQ] 尚未连接，图片没有发出去（/qq enable）");
                return false;
            }
            String text = message.replace("[QQ图片]", "").trim();
            String player = net.minecraft.client.Minecraft.getInstance().player != null
                    ? net.minecraft.client.Minecraft.getInstance().player.getScoreboardName() : "";
            if (!text.isEmpty()) {
                client.sendGroup(ChatFormat.formatOutbound(player, text));
            }
            client.sendImages(pending);
            ClipboardImages.clear();
            ClipboardPreview.clear();
            ChatFormat.showLocalSent(text.isEmpty()
                    ? ("[图片 x" + pending.size() + "]")
                    : (text + " [图片 x" + pending.size() + "]"));
            return false;
        }
        if (looksLikeImagePath(message)) {
            ChatFormat.notify("§e[QQ] 发图片请直接在聊天栏按 §fCtrl+V§e 粘贴，再按 Enter 发送");
            return false;
        }

        BridgeConfig config = BridgeConfig.get();
        String prefix = config.prefix;
        if (prefix == null || prefix.isEmpty() || !message.startsWith(prefix)) {
            return true;
        }
        String body = message.substring(prefix.length()).trim();
        if (body.isEmpty()) {
            return true;
        }
        OneBotClient client = QqChatBridge.client();
        if (client == null || !client.isConnected()) {
            ChatFormat.notify("§c[QQ] 尚未连接，消息没有发出去（/qq enable）");
            return false;
        }
        String player = net.minecraft.client.Minecraft.getInstance().player != null
                ? net.minecraft.client.Minecraft.getInstance().player.getScoreboardName() : "";
        client.sendGroup(ChatFormat.formatOutbound(player, body));
        ChatFormat.showLocalSent(body);
        // 返回 false = 取消这条聊天，不发给服务器
        return false;
    }

    private static boolean looksLikeImagePath(String message) {
        String text = message.trim().replace("\"", "");
        if (!ClipboardImages.isImage(text)) {
            return false;
        }
        return text.contains("\\") || text.contains("/") || text.startsWith("file:");
    }
}
