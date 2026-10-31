package com.qqchatbridge.config;

import com.google.gson.JsonElement;
import com.google.gson.JsonParser;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;

/**
 * 从 relay 的 /client-config 拉取管理员下发的配置。
 *
 * <p>只覆盖连接、群组、模板等字段，不会覆盖本地的 remoteConfigUrl / remoteConfigToken。</p>
 */
public final class RemoteConfig {

    private static final HttpClient HTTP = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(6))
            .build();

    private RemoteConfig() {
    }

    public static void refreshAsync(Runnable after) {
        Thread thread = new Thread(() -> {
            try {
                refresh();
            } catch (Exception ignored) {
            }
            if (after != null) {
                try {
                    after.run();
                } catch (Exception ignored) {
                }
            }
        }, "qqchatbridge-remote-config");
        thread.setDaemon(true);
        thread.start();
    }

    public static void refresh() throws Exception {
        BridgeConfig config = BridgeConfig.get();
        String url = config.remoteConfigUrl;
        if (url == null || url.isBlank()) {
            return;
        }
        String token = config.remoteConfigToken == null || config.remoteConfigToken.isBlank()
                ? config.token : config.remoteConfigToken;

        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(url))
                .timeout(Duration.ofSeconds(8))
                .GET();
        if (token != null && !token.isBlank()) {
            builder.header("Authorization", "Bearer " + token);
        }
        HttpResponse<String> response = HTTP.send(builder.build(),
                HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() != 200) {
            return;
        }
        JsonElement element = JsonParser.parseString(response.body());
        if (element.isJsonObject()) {
            BridgeConfig.applyRemote(element.getAsJsonObject());
        }
    }
}
