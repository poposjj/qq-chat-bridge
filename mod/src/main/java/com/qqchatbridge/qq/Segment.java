package com.qqchatbridge.qq;

/**
 * 一条 QQ 消息被解析后的片段。
 * 只保留文字、表情、图片、语音四种，其余类型在解析阶段就被丢弃。
 */
public record Segment(Type type, String text, String mediaId, String fileId, String url) {

    public enum Type {
        TEXT,
        FACE,
        IMAGE,
        VOICE
    }

    public static Segment text(String text) {
        return new Segment(Type.TEXT, text, "", "", "");
    }

    public static Segment face(String id, String name) {
        return new Segment(Type.FACE, name, "", id, "");
    }

    public static Segment image(String fileId, String url) {
        return new Segment(Type.IMAGE, "", "", fileId, url);
    }

    public static Segment voice(String fileId, String url) {
        return new Segment(Type.VOICE, "", "", fileId, url);
    }

    public Segment withMediaId(String newMediaId) {
        return new Segment(type, text, newMediaId, fileId, url);
    }
}
