package com.example.tacsit.network;

import android.content.Context;
import android.content.SharedPreferences;
import androidx.security.crypto.EncryptedSharedPreferences;
import androidx.security.crypto.MasterKey;
import android.util.Log;

/**
 * Защищённое хранилище сессии клиента с шифрованием.
 * Использует EncryptedSharedPreferences для персистентности токенов между сеансами.
 */
public final class SessionManager {
    private static final String TAG = "SessionManager";
    private static final String PREF_FILE_NAME = "tacit_session_encrypted";
    private static final String KEY_ACCESS_TOKEN = "access_token";
    private static final String KEY_REFRESH_TOKEN = "refresh_token";
    private static final String KEY_ROLE = "user_role";
    private static final String KEY_SERVER_URL = "server_url";
    private static final String KEY_USER_ID = "user_id";
    private static final String KEY_HMAC_KEY = "hmac_key";
    private static final String KEY_PACKET_SEQUENCE = "packet_sequence";

    private static SharedPreferences encryptedPreferences;
    private static volatile String accessToken;
    private static volatile String refreshToken;
    private static volatile String role;
    private static volatile String serverUrl;
    private static volatile Integer userId;
    private static volatile String hmacKey;

    private SessionManager() {
    }

    /**
     * Инициализация SessionManager с контекстом приложения.
     * Должна быть вызвана в Application.onCreate() или перед первым использованием.
     */
    public static void init(Context context) {
        if (encryptedPreferences != null) {
            return;
        }

        try {
            MasterKey masterKey = new MasterKey.Builder(context)
                    .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                    .build();

            encryptedPreferences = EncryptedSharedPreferences.create(
                    context,
                    PREF_FILE_NAME,
                    masterKey,
                    EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                    EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM);

            // Загружаем сохранённые токены при инициализации
            accessToken = encryptedPreferences.getString(KEY_ACCESS_TOKEN, null);
            refreshToken = encryptedPreferences.getString(KEY_REFRESH_TOKEN, null);
            role = encryptedPreferences.getString(KEY_ROLE, null);
            serverUrl = encryptedPreferences.getString(KEY_SERVER_URL, null);
            String storedUserId = encryptedPreferences.getString(KEY_USER_ID, null);
            userId = storedUserId == null ? null : Integer.parseInt(storedUserId);
            hmacKey = encryptedPreferences.getString(KEY_HMAC_KEY, null);

            Log.d(TAG, "SessionManager инициализирован");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка инициализации EncryptedSharedPreferences", e);
        }
    }

    /**
     * Сохранить сессию (токены, роль, URL сервера)
     */
    public static void setSession(String token, String refresh, String userRole, String url) {
        setSession(token, refresh, userRole, url, null, null);
    }

    public static void setSession(String token, String refresh, String userRole, String url, Integer resolvedUserId, String resolvedHmacKey) {
        if (encryptedPreferences == null) {
            Log.w(TAG, "SessionManager не инициализирован! Использую in-memory storage.");
            accessToken = token;
            refreshToken = refresh;
            role = userRole;
            serverUrl = url;
            userId = resolvedUserId;
            hmacKey = resolvedHmacKey;
            return;
        }

        try {
            SharedPreferences.Editor editor = encryptedPreferences.edit();
            editor.putString(KEY_ACCESS_TOKEN, token);
            editor.putString(KEY_REFRESH_TOKEN, refresh);
            editor.putString(KEY_ROLE, userRole);
            editor.putString(KEY_SERVER_URL, url);
            editor.putString(KEY_USER_ID, resolvedUserId == null ? null : String.valueOf(resolvedUserId));
            editor.putString(KEY_HMAC_KEY, resolvedHmacKey);
            editor.apply();

            accessToken = token;
            refreshToken = refresh;
            role = userRole;
            serverUrl = url;
            userId = resolvedUserId;
            hmacKey = resolvedHmacKey;

            Log.d(TAG, "Сессия сохранена (роль: " + userRole + ")");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка сохранения сессии", e);
        }
    }

    /**
     * Альтернативный метод для совместимости (без URL)
     */
    public static void setSession(String token, String refresh, String userRole) {
        setSession(token, refresh, userRole, serverUrl, userId, hmacKey);
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

    public static String getServerUrl() {
        return serverUrl;
    }

    public static Integer getUserId() {
        return userId;
    }

    public static String getHmacKey() {
        return hmacKey;
    }

    public static long nextPacketSequence() {
        long next = 1L;
        if (encryptedPreferences != null) {
            next = encryptedPreferences.getLong(KEY_PACKET_SEQUENCE, 0L) + 1L;
            encryptedPreferences.edit().putLong(KEY_PACKET_SEQUENCE, next).apply();
        }
        return next;
    }

    public static boolean isAuthenticated() {
        return accessToken != null && !accessToken.isEmpty();
    }

    /**
     * Очистить сессию (при logout или истечении токена)
     */
    public static void clear() {
        if (encryptedPreferences == null) {
            accessToken = null;
            refreshToken = null;
            role = null;
            serverUrl = null;
            return;
        }

        try {
            SharedPreferences.Editor editor = encryptedPreferences.edit();
            editor.remove(KEY_ACCESS_TOKEN);
            editor.remove(KEY_REFRESH_TOKEN);
            editor.remove(KEY_ROLE);
            editor.remove(KEY_SERVER_URL);
            editor.remove(KEY_USER_ID);
            editor.remove(KEY_HMAC_KEY);
            editor.remove(KEY_PACKET_SEQUENCE);
            editor.apply();

            accessToken = null;
            refreshToken = null;
            role = null;
            serverUrl = null;
            userId = null;
            hmacKey = null;

            Log.d(TAG, "Сессия очищена");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка очистки сессии", e);
        }
    }
}

