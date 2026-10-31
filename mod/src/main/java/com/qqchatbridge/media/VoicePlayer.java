package com.qqchatbridge.media;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.sound.sampled.AudioInputStream;
import javax.sound.sampled.AudioSystem;
import javax.sound.sampled.Clip;
import javax.sound.sampled.LineEvent;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * 用 Java 自带的声音 API 播放 WAV（NapCat 的 get_record 能把 QQ 语音转成 wav）。
 * 不依赖 Minecraft 的声音系统，也不依赖外部播放器。
 */
public final class VoicePlayer {

    private static final Logger LOGGER = LoggerFactory.getLogger("qqchatbridge");
    private static final List<Clip> ACTIVE = new CopyOnWriteArrayList<>();

    private VoicePlayer() {
    }

    public static void play(Path wav) throws Exception {
        AudioInputStream stream = AudioSystem.getAudioInputStream(wav.toFile());
        Clip clip = AudioSystem.getClip();
        clip.addLineListener(event -> {
            if (event.getType() == LineEvent.Type.STOP) {
                clip.close();
                ACTIVE.remove(clip);
            }
        });
        clip.open(stream);
        ACTIVE.add(clip);
        clip.start();
        LOGGER.info("播放语音：{}", wav.getFileName());
    }

    public static void stopAll() {
        for (Clip clip : ACTIVE) {
            try {
                clip.stop();
                clip.close();
            } catch (Exception ignored) {
            }
        }
        ACTIVE.clear();
    }
}
