package com.example.tacsit;

import android.app.Application;
import android.util.Log;
import com.example.tacsit.network.SessionManager;

/**
 * Custom Application класс для инициализации глобальных компонентов.
 */
public class TacitApplication extends Application {
    private static final String TAG = "TacitApplication";

    @Override
    public void onCreate() {
        super.onCreate();

        // Инициализируем SessionManager с шифрованным хранилищем
        try {
            SessionManager.init(this);
            Log.d(TAG, "SessionManager инициализирован");
        } catch (Exception e) {
            Log.e(TAG, "Ошибка при инициализации SessionManager", e);
        }
    }
}
