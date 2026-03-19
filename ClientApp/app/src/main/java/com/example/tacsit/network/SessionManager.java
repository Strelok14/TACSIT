package com.example.tacsit.network;

/**
 * In-memory хранилище сессии клиента.
 * Для production рекомендуется заменить на EncryptedSharedPreferences для персистентности после закрытия приложения.
 */
public final class SessionManager {

    private static volatile String accessToken;
    private static volatile String refreshToken;
    private static volatile String role;

    private SessionManager() {
    }

    public static void setSession(String token, String refresh, String userRole) {
        accessToken = token;
        refreshToken = refresh;
        role = userRole;
    }

    public static String getAccessToken() {
        return accessToken;
    }

    public static String getRefreshToken() {
        return refreshToken;
    }

    public static String getRole() {
        return role;
    }

    public static void clear() {
        accessToken = null;
        refreshToken = null;
        role = null;
    }
}

