package com.qqchatbridge.qq;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * QQ 表情（face）表：id ↔ 名称。
 *
 * <p>数据来自 NapCat 自带的 QQ 表情表（faces.json，329 项）。
 * 收到 face 段时渲染成 {@code [微笑]} 这样的文本；发送时把文本里的
 * {@code [微笑]} 还原成 face 段，实现表情双向互通。</p>
 */
public final class FaceTable {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");
    private static final Map<String, String> ID_TO_NAME = new HashMap<>();
    private static final Map<String, String> NAME_TO_ID = new HashMap<>();

    static {
        try (InputStream stream = FaceTable.class.getResourceAsStream("/assets/qqchatbridge/faces.json")) {
            if (stream != null) {
                JsonObject object = JsonParser.parseReader(new InputStreamReader(stream, StandardCharsets.UTF_8))
                        .getAsJsonObject();
                for (Map.Entry<String, JsonElement> entry : object.entrySet()) {
                    String name = entry.getValue().getAsString();
                    ID_TO_NAME.put(entry.getKey(), name);
                    NAME_TO_ID.putIfAbsent(name, entry.getKey());
                }
            }
        } catch (Exception e) {
            LOGGER.warn("读取 QQ 表情表失败", e);
        }
        LOGGER.info("已加载 {} 个 QQ 表情", ID_TO_NAME.size());
    }

    private FaceTable() {
    }

    public static String nameOf(String id) {
        String name = ID_TO_NAME.get(id);
        return name == null ? "" : name;
    }

    public static String idOf(String name) {
        return NAME_TO_ID.get(name);
    }

    public static boolean contains(String name) {
        return NAME_TO_ID.containsKey(name);
    }

    /**
     * 把聊天文本按 {@code [表情名]} 切分成文本段和表情段。
     */
    public static List<Segment> split(String text) {
        List<Segment> segments = new ArrayList<>();
        StringBuilder buffer = new StringBuilder();
        int index = 0;
        while (index < text.length()) {
            char c = text.charAt(index);
            if (c == '[') {
                int end = text.indexOf(']', index + 1);
                if (end > index + 1 && end - index <= 12) {
                    String name = text.substring(index + 1, end);
                    String id = idOf(name);
                    if (id != null) {
                        if (buffer.length() > 0) {
                            segments.add(Segment.text(buffer.toString()));
                            buffer.setLength(0);
                        }
                        segments.add(Segment.face(id, name));
                        index = end + 1;
                        continue;
                    }
                }
            }
            buffer.append(c);
            index++;
        }
        if (buffer.length() > 0) {
            segments.add(Segment.text(buffer.toString()));
        }
        return segments;
    }
}
