package com.qqchatbridge.config;

/**
 * 构建时写入的中转默认值（私用构建）。
 * 公开仓库版本应为空字符串。
 */
public final class BuildDefaults {

    /** 形如 1.2.3.4:28574，留空表示不启用内置中转。 */
    public static final String RELAY_BASE = "";
    /** relay 共享 Token，必须与 relay.ini 一致。 */
    public static final String RELAY_TOKEN = "";
    /** 默认主群，0 表示等下发配置。 */
    public static final long DEFAULT_GROUP_ID = 0L;

    private BuildDefaults() {
    }
}