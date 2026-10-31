package com.qqchatbridge.media;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.imageio.ImageIO;
import java.awt.Graphics2D;
import java.awt.Image;
import java.awt.Toolkit;
import java.awt.datatransfer.Clipboard;
import java.awt.datatransfer.DataFlavor;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * 剪贴板图片捕获（支持一次粘贴多张）。
 *
 * <p>聊天栏 Ctrl+V 时调用：剪贴板里是图片或图片文件时，存到
 * {@code config/qqchatbridge/media/clipboard/} 并加入待发送列表；
 * 原版粘贴被取消，玩家需要用 {@code /qq image} 发送。</p>
 */
public final class ClipboardImages {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");
    private static final int MAX_PENDING = 9;
    private static final List<Path> PENDING = new CopyOnWriteArrayList<>();

    private ClipboardImages() {
    }

    public static List<Path> pending() {
        return List.copyOf(PENDING);
    }

    public static void clear() {
        PENDING.clear();
    }

    public static Path first() {
        return PENDING.isEmpty() ? null : PENDING.get(0);
    }

    /** @return 本次新捕获的图片；剪贴板里没有图片时返回空列表。 */
    public static List<Path> capture() {
        List<Path> found = new ArrayList<>();
        try {
            Clipboard clipboard = Toolkit.getDefaultToolkit().getSystemClipboard();

            if (clipboard.isDataFlavorAvailable(DataFlavor.imageFlavor)) {
                Object data = clipboard.getData(DataFlavor.imageFlavor);
                if (data instanceof Image image) {
                    BufferedImage buffered = toBuffered(image);
                    Path target = newTarget(".png");
                    ImageIO.write(buffered, "png", target.toFile());
                    found.add(target);
                }
            } else if (clipboard.isDataFlavorAvailable(DataFlavor.javaFileListFlavor)) {
                Object data = clipboard.getData(DataFlavor.javaFileListFlavor);
                if (data instanceof List<?> files) {
                    for (Object item : files) {
                        if (item instanceof File file && isImage(file.getName())) {
                            found.add(file.toPath());
                        }
                    }
                }
            }
        } catch (Exception e) {
            LOGGER.warn("读取剪贴板图片失败", e);
        }

        for (Path path : found) {
            if (PENDING.size() >= MAX_PENDING) {
                LOGGER.info("待发送图片已达上限 {} 张，忽略：{}", MAX_PENDING, path);
                continue;
            }
            if (!PENDING.contains(path)) {
                PENDING.add(path);
                LOGGER.info("已捕获图片：{}", path);
            }
        }
        return found;
    }

    private static Path newTarget(String extension) throws Exception {
        Path dir = MediaStore.directory().resolve("clipboard");
        Files.createDirectories(dir);
        return dir.resolve("clip-" + System.currentTimeMillis() + "-"
                + Integer.toHexString((int) (Math.random() * 0xFFFF)) + extension);
    }

    private static BufferedImage toBuffered(Image image) {
        if (image instanceof BufferedImage buffered) {
            return buffered;
        }
        BufferedImage result = new BufferedImage(
                image.getWidth(null), image.getHeight(null), BufferedImage.TYPE_INT_ARGB);
        Graphics2D graphics = result.createGraphics();
        graphics.drawImage(image, 0, 0, null);
        graphics.dispose();
        return result;
    }

    public static boolean isImage(String name) {
        String lower = name.toLowerCase();
        return lower.endsWith(".png") || lower.endsWith(".jpg") || lower.endsWith(".jpeg")
                || lower.endsWith(".gif") || lower.endsWith(".webp") || lower.endsWith(".bmp");
    }
}
