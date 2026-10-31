package com.qqchatbridge.chat;

import com.qqchatbridge.config.BridgeConfig;
import com.qqchatbridge.qq.GroupMember;
import com.qqchatbridge.qq.Segment;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.ChatFormatting;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.ClickEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.HoverEvent;
import net.minecraft.network.chat.MutableComponent;
import net.minecraft.network.chat.Style;
import net.minecraft.network.chat.TextColor;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

/**
 * 聊天栏渲染。格式由管理员在启动器里用模板配置：
 * <pre>
 * 游戏内：{time} {prefix} {role} {title} {level} {name} {qq} {group} {message}
 * 发到QQ：{player} {message} {time} {world} {group}
 * 颜色用 &amp; / § 代码，例如 &amp;7、&amp;a。
 * </pre>
 */
public final class ChatFormat {

    public static final String DEFAULT_GAME_FORMAT =
            "&7{time} &d{prefix}{role}{title}{level}{name}&8({qq})&7: {message}";
    public static final String DEFAULT_QQ_FORMAT = "[{player}] {message}";

    private static final boolean CONVERSE_LOADED = FabricLoader.getInstance().isModLoaded("converse");

    private static final int[] NAME_COLORS = {
            0x55FFFF, 0x55FF55, 0xFFFF55, 0xFF55FF,
            0xFFAA00, 0x00AAFF, 0xFF5555, 0xAA00FF,
            0x00FFAA, 0xFF8800
    };

    private ChatFormat() {
    }

    /* ---------------- 群消息 → 游戏 ---------------- */

    public static void showGroupMessage(GroupMember member, java.util.List<Segment> segments) {
        showGroupMessage(member, segments, "");
    }

    public static void showGroupMessage(GroupMember member, java.util.List<Segment> segments,
            String groupTag) {
        BridgeConfig config = BridgeConfig.get();
        String template = config.gameFormat == null || config.gameFormat.isBlank()
                ? DEFAULT_GAME_FORMAT : config.gameFormat;

        MutableComponent line = Component.empty();
        StringBuilder literal = new StringBuilder();
        ChatFormatting current = ChatFormatting.WHITE;

        for (int i = 0; i < template.length(); i++) {
            char c = template.charAt(i);
            if ((c == '&' || c == '§') && i + 1 < template.length()) {
                ChatFormatting format = colorByCode(Character.toLowerCase(template.charAt(i + 1)));
                if (format != null) {
                    flush(line, literal, current);
                    current = format;
                    i++;
                    continue;
                }
            }
            if (c == '{') {
                int end = template.indexOf('}', i);
                if (end > i) {
                    String token = template.substring(i + 1, end);
                    if (isToken(token)) {
                        flush(line, literal, current);
                        line.append(tokenComponent(token, member, segments, groupTag, config));
                        i = end;
                        continue;
                    }
                }
            }
            literal.append(c);
        }
        flush(line, literal, current);
        deliver(line);
    }

    private static void flush(MutableComponent line, StringBuilder literal, ChatFormatting format) {
        if (literal.length() > 0) {
            line.append(Component.literal(literal.toString()).withStyle(format));
            literal.setLength(0);
        }
    }

    private static boolean isToken(String token) {
        switch (token) {
            case "time":
            case "prefix":
            case "role":
            case "title":
            case "level":
            case "name":
            case "qq":
            case "group":
            case "message":
                return true;
            default:
                return false;
        }
    }

    private static Component tokenComponent(String token, GroupMember member,
            java.util.List<Segment> segments, String groupTag, BridgeConfig config) {
        switch (token) {
            case "time":
                return Component.literal(time()).withStyle(ChatFormatting.DARK_GRAY);
            case "prefix":
                return Component.literal(config.bridgePrefix).withStyle(ChatFormatting.DARK_PURPLE);
            case "role":
                if (member.isOwner() || member.isAdmin()) {
                    return Component.literal("[" + member.roleName() + "] ")
                            .withStyle(member.isOwner() ? ChatFormatting.RED : ChatFormatting.GREEN);
                }
                return Component.empty();
            case "title":
                if (member.title() != null && !member.title().isBlank()) {
                    return Component.literal("[" + member.title() + "] ").withStyle(ChatFormatting.GOLD);
                }
                return Component.empty();
            case "level":
                if (member.level() != null && !member.level().isBlank()) {
                    return Component.literal("Lv." + member.level() + " ").withStyle(ChatFormatting.DARK_GRAY);
                }
                return Component.empty();
            case "name":
                return Component.literal(member.displayName()).withStyle(style -> {
                    Style applied = style.withColor(TextColor.fromRgb(colorFor(member.userId())));
                    applied = applied.withHoverEvent(new HoverEvent.ShowText(tooltip(member)));
                    return applied;
                });
            case "qq":
                return Component.literal(String.valueOf(member.userId())).withStyle(ChatFormatting.DARK_GRAY);
            case "group":
                return Component.literal(groupTag == null ? "" : groupTag).withStyle(ChatFormatting.DARK_AQUA);
            case "message":
                return messageComponent(segments);
            default:
                return Component.empty();
        }
    }

    private static Component messageComponent(java.util.List<Segment> segments) {
        MutableComponent body = Component.empty();
        for (Segment segment : segments) {
            switch (segment.type()) {
                case TEXT:
                    body.append(Component.literal(segment.text()).withStyle(ChatFormatting.WHITE));
                    break;
                case FACE:
                    body.append(Component.literal("[" + segment.text() + "]")
                            .withStyle(ChatFormatting.YELLOW));
                    break;
                case IMAGE:
                    body.append(clickable("[图片]", ChatFormatting.AQUA,
                            "点击查看图片（来自 QQ 群）", "/qq open " + segment.mediaId()));
                    if (CONVERSE_LOADED && segment.url() != null && !segment.url().isBlank()) {
                        body.append(Component.literal(" " + segment.url()).withStyle(ChatFormatting.DARK_GRAY));
                    }
                    break;
                case VOICE:
                    body.append(clickable("[语音]", ChatFormatting.GOLD,
                            "点击在游戏内播放语音", "/qq play " + segment.mediaId()));
                    break;
                default:
                    break;
            }
        }
        return body;
    }

    /* ---------------- 游戏 → QQ ---------------- */

    /** 按管理员模板把玩家聊天包装成发往 QQ 的文本。 */
    public static String formatOutbound(String player, String message) {
        BridgeConfig config = BridgeConfig.get();
        String template = config.qqFormat == null || config.qqFormat.isBlank()
                ? DEFAULT_QQ_FORMAT : config.qqFormat;
        StringBuilder out = new StringBuilder();
        int i = 0;
        while (i < template.length()) {
            char c = template.charAt(i);
            if (c == '{') {
                int end = template.indexOf('}', i);
                if (end > i) {
                    String token = template.substring(i + 1, end);
                    String value = outboundToken(token, player, message, config);
                    if (value != null) {
                        out.append(value);
                        i = end + 1;
                        continue;
                    }
                }
            }
            out.append(c);
            i++;
        }
        return out.toString();
    }

    /** 单次扫描替换：占位符只是变量，用户消息里的花括号不会被二次展开。 */
    private static String outboundToken(String token, String player, String message,
            BridgeConfig config) {
        switch (token) {
            case "player":
                return player == null ? "" : player;
            case "message":
                return message == null ? "" : message;
            case "time":
                return time();
            case "world":
                return worldName();
            case "group":
                return String.valueOf(config.groupId);
            default:
                return null;
        }
    }
    private static String worldName() {
        try {
            Minecraft minecraft = Minecraft.getInstance();
            if (minecraft.level == null) {
                return "";
            }
            return String.valueOf(minecraft.level.dimension());
        } catch (Exception e) {
            return "";
        }
    }

    private static MutableComponent clickable(String label, ChatFormatting color, String hover, String command) {
        return Component.literal(label).withStyle(style -> style
                .withColor(color)
                .withUnderlined(true)
                .withHoverEvent(new HoverEvent.ShowText(Component.literal(hover)))
                .withClickEvent(new ClickEvent.RunCommand(command)));
    }

    /** 本地回显：自己通过 :前缀 或 /qq say 发出的消息。 */
    public static void showLocalSent(String text) {
        BridgeConfig config = BridgeConfig.get();
        MutableComponent line = Component.empty();
        line.append(Component.literal(config.bridgePrefix).withStyle(ChatFormatting.DARK_PURPLE));
        line.append(Component.literal("你").withStyle(ChatFormatting.AQUA));
        line.append(Component.literal(": " + text).withStyle(ChatFormatting.GRAY));
        deliverLocal(line);
    }

    public static void notify(String message) {
        deliverLocal(parse(message));
    }

    /** 解析 § / & 颜色代码（只取消息最前面的颜色，其余代码剥掉）。 */
    public static Component parse(String text) {
        ChatFormatting color = ChatFormatting.GRAY;
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if ((c == '§' || c == '&') && i + 1 < text.length()) {
                ChatFormatting format = colorByCode(Character.toLowerCase(text.charAt(i + 1)));
                if (format != null && out.length() == 0) {
                    color = format;
                }
                i++;
                continue;
            }
            out.append(c);
        }
        return Component.literal(out.toString()).withStyle(color);
    }

    private static ChatFormatting colorByCode(char code) {
        return switch (code) {
            case '0' -> ChatFormatting.BLACK;
            case '1' -> ChatFormatting.DARK_BLUE;
            case '2' -> ChatFormatting.DARK_GREEN;
            case '3' -> ChatFormatting.DARK_AQUA;
            case '4' -> ChatFormatting.DARK_RED;
            case '5' -> ChatFormatting.DARK_PURPLE;
            case '6' -> ChatFormatting.GOLD;
            case '7' -> ChatFormatting.GRAY;
            case '8' -> ChatFormatting.DARK_GRAY;
            case '9' -> ChatFormatting.BLUE;
            case 'a' -> ChatFormatting.GREEN;
            case 'b' -> ChatFormatting.AQUA;
            case 'c' -> ChatFormatting.RED;
            case 'd' -> ChatFormatting.LIGHT_PURPLE;
            case 'e' -> ChatFormatting.YELLOW;
            case 'f' -> ChatFormatting.WHITE;
            case 'k' -> ChatFormatting.OBFUSCATED;
            case 'l' -> ChatFormatting.BOLD;
            case 'm' -> ChatFormatting.STRIKETHROUGH;
            case 'n' -> ChatFormatting.UNDERLINE;
            case 'o' -> ChatFormatting.ITALIC;
            case 'r' -> ChatFormatting.RESET;
            default -> null;
        };
    }

    private static MutableComponent tooltip(GroupMember member) {
        MutableComponent tip = Component.literal("QQ：" + member.userId()).withStyle(ChatFormatting.GRAY);
        if (member.nickname() != null && !member.nickname().isBlank()) {
            tip.append("\n").append(Component.literal("昵称：" + member.nickname()).withStyle(ChatFormatting.GRAY));
        }
        if (member.card() != null && !member.card().isBlank()) {
            tip.append("\n").append(Component.literal("群名片：" + member.card()).withStyle(ChatFormatting.GRAY));
        }
        if (member.title() != null && !member.title().isBlank()) {
            tip.append("\n").append(Component.literal("头衔：" + member.title()).withStyle(ChatFormatting.GOLD));
        }
        if (member.level() != null && !member.level().isBlank()) {
            tip.append("\n").append(Component.literal("等级：Lv." + member.level()).withStyle(ChatFormatting.GRAY));
        }
        tip.append("\n").append(Component.literal("身份：" + member.roleName()).withStyle(ChatFormatting.GRAY));
        return tip;
    }

    private static int colorFor(long userId) {
        int index = (int) Math.floorMod(userId, (long) NAME_COLORS.length);
        return NAME_COLORS[index];
    }

    private static String time() {
        String pattern = BridgeConfig.get().dateFormat;
        try {
            return LocalTime.now().format(DateTimeFormatter.ofPattern(pattern));
        } catch (Exception e) {
            return LocalTime.now().format(DateTimeFormatter.ofPattern("HH:mm"));
        }
    }

    private static void deliver(Component component) {
        Minecraft minecraft = Minecraft.getInstance();
        if (minecraft == null || minecraft.player == null || minecraft.player.connection == null) {
            return;
        }
        boolean broadcast = BridgeConfig.get().broadcast;
        minecraft.execute(() -> {
            if (minecraft.player == null || minecraft.player.connection == null) {
                return;
            }
            if (broadcast) {
                minecraft.player.connection.sendChat(component.getString());
            } else {
                minecraft.player.sendSystemMessage(component);
            }
        });
    }

    private static void deliverLocal(Component component) {
        Minecraft minecraft = Minecraft.getInstance();
        if (minecraft == null || minecraft.player == null) {
            return;
        }
        minecraft.execute(() -> {
            if (minecraft.player != null) {
                minecraft.player.sendSystemMessage(component);
            }
        });
    }
}
