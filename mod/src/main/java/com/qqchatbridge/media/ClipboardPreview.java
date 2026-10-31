package com.qqchatbridge.media;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * 聊天栏右下角的剪贴板图片预览（多张时显示第一张 + 数量）。
 */
public final class ClipboardPreview {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");
    private static final Identifier TEXTURE_ID =
            Identifier.fromNamespaceAndPath("qqchatbridge", "clipboard/preview");

    private static int width;
    private static int height;
    private static boolean visible;

    private ClipboardPreview() {
    }

    public static void show(Path file) {
        try (InputStream input = Files.newInputStream(file)) {
            NativeImage image = NativeImage.read(input);
            DynamicTexture texture = new DynamicTexture(() -> "qqchatbridge clipboard", image);
            texture.upload();
            Minecraft.getInstance().getTextureManager().register(TEXTURE_ID, texture);
            width = image.getWidth();
            height = image.getHeight();
            visible = true;
        } catch (Exception e) {
            LOGGER.warn("加载剪贴板预览失败", e);
        }
    }

    public static void clear() {
        visible = false;
    }

    public static void render(GuiGraphicsExtractor graphics) {
        if (!visible || width <= 0 || height <= 0) {
            return;
        }
        Minecraft minecraft = Minecraft.getInstance();
        int screenWidth = minecraft.getWindow().getGuiScaledWidth();
        int screenHeight = minecraft.getWindow().getGuiScaledHeight();

        float scale = Math.min(1.0f, Math.min(160.0f / width, 120.0f / height));
        int drawWidth = Math.max(1, (int) (width * scale));
        int drawHeight = Math.max(1, (int) (height * scale));
        int x = screenWidth - drawWidth - 12;
        int y = screenHeight - 52 - drawHeight;

        graphics.fill(x - 2, y - 2, x + drawWidth + 2, y + drawHeight + 2, 0x88000000);
        graphics.blit(TEXTURE_ID, x, y, drawWidth, drawHeight, 0.0f, 0.0f, (float) width, (float) height);

        int count = ClipboardImages.pending().size();
        if (count > 1) {
            String badge = "共 " + count + " 张";
            int badgeWidth = minecraft.font.width(badge) + 6;
            graphics.fill(x + drawWidth - badgeWidth, y + drawHeight - 12,
                    x + drawWidth, y + drawHeight, 0xCC000000);
            graphics.text(minecraft.font, badge, x + drawWidth - badgeWidth + 3,
                    y + drawHeight - 10, 0xFFFFFF);
        }
    }
}
