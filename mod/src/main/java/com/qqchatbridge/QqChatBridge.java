package com.qqchatbridge;

import com.qqchatbridge.chat.ChatHook;
import com.qqchatbridge.chat.ChatFormat;
import com.qqchatbridge.command.QqCommand;
import com.qqchatbridge.config.BridgeConfig;
import com.qqchatbridge.config.RemoteConfig;
import com.qqchatbridge.qq.OneBotClient;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * QQ Chat Bridge：纯客户端 Fabric 模组，把 Minecraft 聊天栏与 QQ 群（OneBot 11）双向接通。
 */
public class QqChatBridge implements ClientModInitializer {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");
    private static OneBotClient client;

    public static OneBotClient client() {
        return client;
    }

    @Override
    public void onInitializeClient() {
        BridgeConfig config = BridgeConfig.get();
        client = new OneBotClient();

        ChatHook.register();
        QqCommand.register();

        ClientLifecycleEvents.CLIENT_STOPPING.register(minecraft -> {
            if (client != null) {
                client.stop();
            }
        });

        // 进入世界后再连：这样“已连接”的提示能正常显示在聊天栏里
        ClientPlayConnectionEvents.JOIN.register((handler, sender, minecraft) -> {
            // 进世界时再拉一次下发配置，管理员改完玩家重进世界即可生效
            RemoteConfig.refreshAsync(() -> {
                BridgeConfig current = BridgeConfig.get();
                if (current.groupId == 0L) {
                    ChatFormat.notify("§e[QQ] 管理员还没配置群号，模组不会连接");
                    return;
                }
                if (current.autoConnect && !client.isConnected()) {
                    client.start();
                } else if (client.isConnected()) {
                    ChatFormat.notify("§a[QQ] 已连接群聊："
                            + (client.groupName().isBlank() ? String.valueOf(current.groupId) : client.groupName()));
                }
            });
        });

        RemoteConfig.refreshAsync(() -> {
            BridgeConfig current = BridgeConfig.get();
            if (current.autoConnect && current.groupId != 0L) {
                client.start();
            }
        });
        LOGGER.info("QQ Chat Bridge 初始化完成，群号={}，ws={}", config.groupId, config.wsUrl);
    }
}
