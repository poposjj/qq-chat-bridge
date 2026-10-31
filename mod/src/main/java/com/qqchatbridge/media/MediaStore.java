package com.qqchatbridge.media;

import com.qqchatbridge.config.BridgeConfig;
import net.fabricmc.loader.api.FabricLoader;

import java.awt.Desktop;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 图片/语音的本地缓存。
 *
 * <p>所有媒体都落在 {@code config/qqchatbridge/media/}，文件名用 URL/文件 id 的哈希，
 * 聊天栏里只放一个短 id，点击时再按 id 找文件。</p>
 */
public final class MediaStore {

    public enum Kind {
        IMAGE,
        VOICE
    }

    public record Entry(Kind kind, String fileId, String url) {
    }

    private static final Map<String, Entry> ENTRIES = new ConcurrentHashMap<>();
    private static final HttpClient HTTP = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .followRedirects(HttpClient.Redirect.NORMAL)
            .build();

    private MediaStore() {
    }

    public static Path directory() {
        Path dir = FabricLoader.getInstance().getConfigDir().resolve("qqchatbridge").resolve("media");
        try {
            Files.createDirectories(dir);
        } catch (IOException ignored) {
        }
        return dir;
    }

    public static String registerImage(String fileId, String url) {
        String id = hash(fileId + "|" + url);
        ENTRIES.putIfAbsent(id, new Entry(Kind.IMAGE, fileId, url));
        return id;
    }

    public static String registerVoice(String fileId, String url) {
        String id = hash("voice|" + fileId);
        ENTRIES.putIfAbsent(id, new Entry(Kind.VOICE, fileId, url));
        return id;
    }

    public static Entry get(String id) {
        return ENTRIES.get(id);
    }

    /** 图片：下载（或复用缓存），返回本地路径。 */
    public static Path ensureImage(String id) throws IOException {
        Entry entry = ENTRIES.get(id);
        if (entry == null) {
            throw new IOException("未知媒体 id");
        }
        Path target = directory().resolve(id + extensionOf(entry.url(), ".png"));
        if (Files.exists(target) && Files.size(target) > 0) {
            return target;
        }
        if (entry.url() == null || entry.url().isBlank()) {
            Path local = resolveLocal(entry.fileId());
            if (local != null && Files.exists(local)) {
                Files.copy(local, target, StandardCopyOption.REPLACE_EXISTING);
                return target;
            }
            throw new IOException("图片没有可下载的 URL（可能需要在 NapCat 里开启图片下载）");
        }
        download(entry.url(), target);
        return target;
    }

    /** 语音：把 get_record 返回的本地路径/URL 落到缓存目录。 */
    public static Path storeVoice(String id, String rawFile) throws IOException {
        Path target = directory().resolve(id + ".wav");
        if (Files.exists(target) && Files.size(target) > 0) {
            return target;
        }
        if (rawFile == null || rawFile.isBlank()) {
            throw new IOException("语音文件为空");
        }
        if (rawFile.startsWith("http://") || rawFile.startsWith("https://")) {
            download(rawFile, target);
            return target;
        }
        Path source = resolveLocal(rawFile);
        if (source == null || !Files.exists(source)) {
            throw new IOException("找不到语音文件：" + rawFile);
        }
        Files.copy(source, target, StandardCopyOption.REPLACE_EXISTING);
        return target;
    }

    public static Path resolveLocal(String raw) {
        try {
            if (raw.startsWith("file://")) {
                return Paths.get(URI.create(raw));
            }
            if (raw.matches("^[A-Za-z]:[\\\\/].*")) {
                return Paths.get(raw);
            }
        } catch (Exception ignored) {
        }
        return null;
    }

    public static void download(String url, Path target) throws IOException {
        try {
            HttpRequest request = HttpRequest.newBuilder(URI.create(url))
                    .timeout(Duration.ofSeconds(30))
                    .header("User-Agent", "Mozilla/5.0 qqchatbridge")
                    .GET()
                    .build();
            HttpResponse<InputStream> response = HTTP.send(request, HttpResponse.BodyHandlers.ofInputStream());
            if (response.statusCode() / 100 != 2) {
                throw new IOException("HTTP " + response.statusCode());
            }
            Files.createDirectories(target.getParent());
            try (InputStream in = response.body()) {
                Files.copy(in, target, StandardCopyOption.REPLACE_EXISTING);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new IOException("下载被中断", e);
        }
    }

    /** 只允许打开缓存目录里的文件，避免被聊天内容诱导打开任意路径。 */
    public static void open(Path path) throws IOException {
        Path base = directory().toAbsolutePath().normalize();
        Path target = path.toAbsolutePath().normalize();
        if (!target.startsWith(base)) {
            throw new IOException("拒绝打开缓存目录外的文件");
        }
        if (Desktop.isDesktopSupported()) {
            Desktop.getDesktop().open(target.toFile());
            return;
        }
        Runtime.getRuntime().exec(new String[]{"rundll32", "url.dll,FileProtocolHandler", target.toString()});
    }

    private static String extensionOf(String url, String fallback) {
        if (url == null) {
            return fallback;
        }
        String path = url;
        int query = path.indexOf('?');
        if (query >= 0) {
            path = path.substring(0, query);
        }
        int dot = path.lastIndexOf('.');
        if (dot >= 0 && dot > path.lastIndexOf('/')) {
            String ext = path.substring(dot).toLowerCase();
            if (ext.matches("\\.(png|jpg|jpeg|gif|webp|bmp)")) {
                return ext;
            }
        }
        return fallback;
    }

    private static String hash(String value) {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-1");
            byte[] bytes = digest.digest(value.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++) {
                sb.append(String.format("%02x", bytes[i]));
            }
            return sb.toString();
        } catch (Exception e) {
            return Integer.toHexString(value.hashCode());
        }
    }
}
