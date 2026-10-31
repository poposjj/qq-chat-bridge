package com.qqchatbridge;

/**
 * 版本标记。
 *
 * <p>用户版固定返回 false；管理员版构建时会把这里改成 true，
 * 解锁多群全部可见。管理员版是独立 jar，不会和用户版一起安装。</p>
 */
public final class Edition {

    private Edition() {
    }

    public static boolean isAdmin() {
        return false;
    }
}
