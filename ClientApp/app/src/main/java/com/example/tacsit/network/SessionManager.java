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

    private static SharedPreferences encryptedPreferences;
    private static volatile String accessToken;
    private static volatile String refreshToken;
    private static volatile String role;
    private static volatile String serverUrl;

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

            Log.d(TAG, "SessionManager инициализирован");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка инициализации EncryptedSharedPreferences", e);
        }
    }

    /**
     * Сохранить сессию (токены, роль, URL сервера)
     */
    public static void setSession(String token, String refresh, String userRole, String url) {
        if (encryptedPreferences == null) {
            Log.w(TAG, "SessionManager не инициализирован! Использую in-memory storage.");
            accessToken = token;
            refreshToken = refresh;
            role = userRole;
            serverUrl = url;
            return;
        }

        try {
            SharedPreferences.Editor editor = encryptedPreferences.edit();
            editor.putString(KEY_ACCESS_TOKEN, token);
            editor.putString(KEY_REFRESH_TOKEN, refresh);
            editor.putString(KEY_ROLE, userRole);
            editor.putString(KEY_SERVER_URL, url);
            editor.apply();

            accessToken = token;
            refreshToken = refresh;
            role = userRole;
            serverUrl = url;

            Log.d(TAG, "Сессия сохранена (роль: " + userRole + ")");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка сохранения сессии", e);
        }
    }

    /**
     * Альтернативный метод для совместимости (без URL)
     */
    public static void setSession(String token, String refresh, String userRole) {
        setSession(token, refresh, userRole, serverUrl);
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
            editor.apply();

            accessToken = null;
            refreshToken = null;
            role = null;
            serverUrl = null;

            Log.d(TAG, "Сессия очищена");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка очистки сессии", e);
        }
    }
}

