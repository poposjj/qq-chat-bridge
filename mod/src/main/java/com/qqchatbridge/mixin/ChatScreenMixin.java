package com.qqchatbridge.mixin;

import com.qqchatbridge.chat.ChatFormat;
import com.qqchatbridge.media.ClipboardImages;
import com.qqchatbridge.media.ClipboardPreview;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.input.KeyEvent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import java.nio.file.Path;
import java.util.List;

/**
 * 聊天栏里的剪贴板图片处理：
 * <ul>
 *     <li>Ctrl+V 时把图片存下来（支持一次多张），输入框插入 [QQ图片]，右侧显示缩略图和数量；</li>
 *     <li>直接按 Enter：ChatHook 会把图片发到 QQ，不会发到服务器。</li>
 * </ul>
 */
@Mixin(ChatScreen.class)
public class ChatScreenMixin {

    @Shadow
    protected EditBox input;

    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void qqchatbridge$onKeyPressed(KeyEvent event, CallbackInfoReturnable<Boolean> cir) {
        // 86 = V
        if (event.key() != 86 || !event.hasControlDown()) {
            return;
        }
        List<Path> captured = ClipboardImages.capture();
        if (captured.isEmpty()) {
            return;
        }
        Path first = ClipboardImages.first();
        if (first != null) {
            ClipboardPreview.show(first);
        }
        if (input != null && !input.getValue().contains("[QQ图片]")) {
            input.insertText("[QQ图片]");
        }
        int count = ClipboardImages.pending().size();
        ChatFormat.notify("§e[QQ] 已附上 " + count + " 张图片，直接按 §fEnter§e 发送到 QQ");
        cir.setReturnValue(true);
        cir.cancel();
    }

    @Inject(method = "extractRenderState", at = @At("TAIL"))
    private void qqchatbridge$renderPreview(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                            float partialTick, CallbackInfo ci) {
        ClipboardPreview.render(graphics);
    }
}
