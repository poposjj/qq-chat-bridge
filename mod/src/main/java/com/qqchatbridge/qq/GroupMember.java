package com.qqchatbridge.qq;

/**
 * QQ 群成员信息。头衔/等级/角色都来自 OneBot 的 sender 或 get_group_member_info。
 */
public record GroupMember(
        long userId,
        String nickname,
        String card,
        String role,
        String level,
        String title
) {

    public static final GroupMember UNKNOWN = new GroupMember(0L, "", "", "member", "", "");

    /** 群名片优先，其次昵称，最后 QQ 号。 */
    public String displayName() {
        if (card != null && !card.isBlank()) {
            return card;
        }
        if (nickname != null && !nickname.isBlank()) {
            return nickname;
        }
        return String.valueOf(userId);
    }

    public boolean isOwner() {
        return "owner".equals(role);
    }

    public boolean isAdmin() {
        return "admin".equals(role);
    }

    public String roleName() {
        if (isOwner()) return "群主";
        if (isAdmin()) return "管理员";
        return "群员";
    }

    public GroupMember withTitle(String newTitle) {
        return new GroupMember(userId, nickname, card, role, level, newTitle);
    }

    public GroupMember withCard(String newCard) {
        return new GroupMember(userId, nickname, newCard, role, level, title);
    }
}
