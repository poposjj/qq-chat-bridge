package com.qqchatbridge.mixin;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.ClickEvent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * 让聊天栏里的 /qq 链接可以单击执行。
 *
 * <p>原版点击 RunCommand 走的是 {@code sendUnattendedCommand}，会绕过 Fabric 的
 * 客户端指令拦截；这里改成调用 {@code sendCommand}，Fabric 就能把 /qq 指令
 * 交给客户端指令分发器本地执行。</p>
 */
@Mixin(Screen.class)
public class ScreenMixin {

    @Inject(method = "defaultHandleClickEvent", at = @At("HEAD"), cancellable = true)
    private static void qqchatbridge$handleClick(ClickEvent event, Minecraft minecraft, Screen screen, CallbackInfo ci) {
        if (!(event instanceof ClickEvent.RunCommand runCommand)) {
            return;
        }
        String command = runCommand.command();
        if (command == null || !command.startsWith("/qq ")) {
            return;
        }
        if (minecraft.player != null && minecraft.player.connection != null) {
            minecraft.player.connection.sendCommand(command.substring(1));
        }
        ci.cancel();
    }
}
