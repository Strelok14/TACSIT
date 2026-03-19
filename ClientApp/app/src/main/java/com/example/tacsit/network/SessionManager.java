package com.example.tacsit.network;

/**
 * Простейшее in-memory хранилище сессии клиента.
 * Для MVP достаточно, в production лучше заменить на EncryptedSharedPreferences.
 */
public final class SessionManager {

    private static volatile String accessToken;
    private static volatile String role;

    private SessionManager() {
    }

    public static void setSession(String token, String userRole) {
        accessToken = token;
        role = userRole;
    }

    public static String getAccessToken() {
        return accessToken;
    }

    public static String getRole() {
        return role;
    }

    public static void clear() {
        accessToken = null;
        role = null;
    }
}
