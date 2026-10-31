package com.qqchatbridge.qq;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import java.util.ArrayList;
import java.util.List;

/**
 * 把 OneBot 消息解析成片段列表。
 *
 * <p>只认文字、表情、图片、语音；文件、视频、卡片、合并转发、戳一戳等
 * 一律丢弃（返回列表里不会出现），如果整条消息都是这些类型就返回空列表。</p>
 */
public final class MessageText {

    private MessageText() {
    }

    public static List<Segment> parse(JsonObject event) {
        List<Segment> segments = new ArrayList<>();
        JsonElement message = event.get("message");
        if (message != null && message.isJsonArray()) {
            fromArray(message.getAsJsonArray(), segments);
        } else if (message != null && message.isJsonPrimitive()) {
            parseCq(message.getAsString(), segments);
        } else {
            String raw = stripCq(optString(event, "raw_message"));
            if (!raw.isBlank()) {
                segments.add(Segment.text(raw));
            }
        }
        return mergeText(segments);
    }

    /** messagePostFormat=string 时，兼容 CQ 码字符串。 */
    private static void parseCq(String raw, List<Segment> segments) {
        if (raw == null || raw.isEmpty()) {
            return;
        }
        int index = 0;
        while (index < raw.length()) {
            int start = raw.indexOf("[CQ:", index);
            if (start < 0) {
                addText(segments, unescape(raw.substring(index)));
                break;
            }
            if (start > index) {
                addText(segments, unescape(raw.substring(index, start)));
            }
            int end = raw.indexOf(']', start);
            if (end < 0) {
                addText(segments, unescape(raw.substring(start)));
                break;
            }
            String body = raw.substring(start + 4, end);
            int comma = body.indexOf(',');
            String type = comma < 0 ? body : body.substring(0, comma);
            String params = comma < 0 ? "" : body.substring(comma + 1);
            if ("face".equals(type)) {
                String id = cqParam(params, "id");
                String name = FaceTable.nameOf(id);
                segments.add(Segment.face(id, name.isBlank() ? "表情" : name));
            } else if ("image".equals(type)) {
                String file = cqParam(params, "file");
                String url = cqParam(params, "url");
                if (!file.isBlank() || !url.isBlank()) {
                    segments.add(Segment.image(file, url));
                }
            } else if ("record".equals(type)) {
                String file = cqParam(params, "file");
                String url = cqParam(params, "url");
                if (!file.isBlank() || !url.isBlank()) {
                    segments.add(Segment.voice(file, url));
                }
            }
            index = end + 1;
        }
    }

    private static String cqParam(String params, String key) {
        for (String part : params.split(",")) {
            int eq = part.indexOf('=');
            if (eq > 0 && part.substring(0, eq).trim().equals(key)) {
                return part.substring(eq + 1).trim();
            }
        }
        return "";
    }

    private static String unescape(String text) {
        return text.replace("&#91;", "[").replace("&#93;", "]")
                .replace("&#44;", ",").replace("&amp;", "&");
    }
    private static void fromArray(JsonArray array, List<Segment> segments) {
        for (JsonElement element : array) {
            if (!element.isJsonObject()) {
                continue;
            }
            JsonObject segment = element.getAsJsonObject();
            String type = optString(segment, "type");
            JsonObject data = segment.has("data") && segment.get("data").isJsonObject()
                    ? segment.getAsJsonObject("data")
                    : new JsonObject();

            switch (type) {
                case "text" -> addText(segments, optString(data, "text"));
                case "at" -> {
                    String name = optString(data, "name");
                    String qq = optString(data, "qq");
                    addText(segments, "@" + (name.isBlank() ? qq : name) + " ");
                }
                case "face" -> {
                    String id = optString(data, "id");
                    String name = FaceTable.nameOf(id);
                    segments.add(Segment.face(id, name.isBlank() ? "表情" : name));
                }
                case "mface" -> {
                    // 商城表情：能拿到文字就用文字，否则用占位
                    String summary = optString(data, "summary");
                    addText(segments, summary.isBlank() ? "[表情]" : summary);
                }
                case "image" -> {
                    String file = optString(data, "file");
                    String url = optString(data, "url");
                    if (!file.isBlank() || !url.isBlank()) {
                        segments.add(Segment.image(file, url));
                    }
                }
                case "record" -> {
                    String file = optString(data, "file");
                    String url = optString(data, "url");
                    if (!file.isBlank() || !url.isBlank()) {
                        segments.add(Segment.voice(file, url));
                    }
                }
                default -> {
                    // 明确屏蔽：file / video / json / xml / forward / markdown /
                    // dice / rps / poke / share / contact / location / music 等
                }
            }
        }
    }

    private static void addText(List<Segment> segments, String text) {
        if (text != null && !text.isEmpty()) {
            segments.add(Segment.text(text));
        }
    }

    /** 合并相邻的文本段，避免聊天栏里出现大量碎片。 */
    private static List<Segment> mergeText(List<Segment> input) {
        List<Segment> merged = new ArrayList<>();
        StringBuilder buffer = new StringBuilder();
        for (Segment segment : input) {
            if (segment.type() == Segment.Type.TEXT) {
                buffer.append(segment.text());
                continue;
            }
            if (buffer.length() > 0) {
                merged.add(Segment.text(buffer.toString()));
                buffer.setLength(0);
            }
            merged.add(segment);
        }
        if (buffer.length() > 0) {
            merged.add(Segment.text(buffer.toString()));
        }
        return merged;
    }

    private static String stripCq(String raw) {
        if (raw == null || raw.isBlank()) {
            return "";
        }
        return raw.replaceAll("\\[CQ:[^]]*]", "").trim();
    }

    public static String optString(JsonObject object, String key) {
        JsonElement element = object.get(key);
        if (element == null || element.isJsonNull()) {
            return "";
        }
        try {
            return element.getAsString();
        } catch (Exception e) {
            return "";
        }
    }
}
