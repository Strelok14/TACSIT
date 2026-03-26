package com.example.tacsit.network;

import android.util.Log;
import com.google.gson.Gson;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.microsoft.signalr.HubConnection;
import com.microsoft.signalr.HubConnectionBuilder;
import com.microsoft.signalr.HubConnectionState;

import java.util.ArrayList;
import java.util.List;

/**
 * Клиент для подключения к SignalR Hub'у сервера позиционирования.
 * Обеспечивает real-time обновления координат маяков через WebSocket.
 */
public class PositioningHubClient {
    private static final String TAG = "PositioningHubClient";

    private final String serverUrl;
    private final String accessToken;
    private HubConnection hubConnection;
    private final List<PositionUpdateListener> listeners = new ArrayList<>();
    private final Gson gson = new Gson();
    private volatile boolean isConnecting = false;

    /**
     * Interface для listener'ов обновления позиций
     */
    public interface PositionUpdateListener {
        void onPositionUpdate(MapPoint position);
        void onConnected(String connectionId);
        void onDisconnected();
        void onError(String message);
    }

    public PositioningHubClient(String serverUrl, String accessToken) {
        this.serverUrl = normalizeWebSocketUrl(serverUrl);
        this.accessToken = accessToken;
    }

    /**
     * Нормализовать URL для WebSocket подключения (ws:// или wss://)
     */
    private String normalizeWebSocketUrl(String url) {
        String normalized = url.trim();
        
        // Удаляем протокол если есть
        if (normalized.startsWith("https://")) {
            normalized = normalized.substring(8);
        } else if (normalized.startsWith("http://")) {
            normalized = normalized.substring(7);
        } else if (normalized.startsWith("wss://")) {
            normalized = normalized.substring(6);
        } else if (normalized.startsWith("ws://")) {
            normalized = normalized.substring(5);
        }

        // Определяем использовать ли wss или ws на основе оригинального URL
        boolean useSecure = url.startsWith("https://") || url.startsWith("wss://");
        String protocol = useSecure ? "wss://" : "ws://";

        // Добавляем порт если его нет
        if (!normalized.contains(":")) {
            normalized += (useSecure ? ":5001" : ":5000");
        }

        return protocol + normalized + "/hubs/positioning";
    }

    /**
     * Подключиться к SignalR Hub'у
     */
    public void connect(Runnable onConnected, Runnable onFailed) {
        if (isConnecting || hubConnection != null) {
            Log.w(TAG, "Уже подключены или подключение в процессе");
            return;
        }

        isConnecting = true;

        try {
            // Создаём HubConnection с JWT token через query string
            hubConnection = HubConnectionBuilder
                    .create(serverUrl + "?access_token=" + accessToken)
                    .withAutomaticReconnect()
                    .build();

            setupEventHandlers();

            // Запускаем подключение
            hubConnection.start().doOnComplete(() -> {
                Log.i(TAG, "✓ Подключены к PositioningHub");
                isConnecting = false;
                notifyConnected(hubConnection.getConnectionId());
                if (onConnected != null) {
                    onConnected.run();
                }
            }).subscribe(
                    () -> {}, // onCompleted
                    error -> {
                        Log.e(TAG, "✗ Ошибка подключения", error);
                        isConnecting = false;
                        notifyError("Ошибка подключения: " + error.getMessage());
                        if (onFailed != null) {
                            onFailed.run();
                        }
                    }
            );

        } catch (Exception e) {
            Log.e(TAG, "Исключение при подключении", e);
            isConnecting = false;
            notifyError("Ошибка при создании подключения: " + e.getMessage());
            if (onFailed != null) {
                onFailed.run();
            }
        }
    }

    /**
     * Настроить обработчики событий от сервера
     */
    private void setupEventHandlers() {
        if (hubConnection == null) {
            return;
        }

        // Обновление позиции маяка
        hubConnection.on("PositionUpdate", message -> {
            try {
                if (message instanceof JsonElement) {
                    JsonObject jsonObj = ((JsonElement) message).getAsJsonObject();
                    MapPoint point = gson.fromJson(jsonObj, MapPoint.class);
                    notifyPositionUpdate(point);
                }
            } catch (Exception e) {
                Log.e(TAG, "Ошибка парсинга PositionUpdate", e);
            }
        }, JsonElement.class);

        // Подтверждение подписки
        hubConnection.on("SubscriptionConfirmed", message -> {
            try {
                if (message instanceof JsonElement) {
                    JsonObject jsonObj = ((JsonElement) message).getAsJsonObject();
                    int beaconId = jsonObj.get("beaconId").getAsInt();
                    Log.d(TAG, "✓ Подписка на маяк " + beaconId + " подтверждена");
                }
            } catch (Exception e) {
                Log.e(TAG, "Ошибка парсинга SubscriptionConfirmed", e);
            }
        }, JsonElement.class);

        // Статус сервера
        hubConnection.on("ServerStatus", message -> {
            try {
                if (message instanceof JsonElement) {
                    Log.d(TAG, "📊 Статус сервера: " + message.toString());
                }
            } catch (Exception e) {
                Log.e(TAG, "Ошибка парсинга ServerStatus", e);
            }
        }, JsonElement.class);

        // Уведомление о подключении
        hubConnection.on("Connected", message -> {
            try {
                if (message instanceof JsonElement) {
                    JsonObject jsonObj = ((JsonElement) message).getAsJsonObject();
                    String msg = jsonObj.get("message").getAsString();
                    Log.i(TAG, "🔌 " + msg);
                }
            } catch (Exception e) {
                Log.e(TAG, "Ошибка парсинга Connected", e);
            }
        }, JsonElement.class);

        // Обработчик отключения
        hubConnection.onClosed(exception -> {
            Log.w(TAG, "🔌 Отключены от PositioningHub");
            notifyDisconnected();
        });
    }

    /**
     * Подписаться на обновления конкретного маяка
     */
    public void subscribeToBeacon(int beaconId) {
        if (hubConnection == null || hubConnection.getState() != HubConnectionState.CONNECTED) {
            Log.w(TAG, "Не подключены к hub'у");
            return;
        }

        try {
            hubConnection.invoke("SubscribeToBeacon", beaconId)
                    .subscribe(
                            () -> Log.d(TAG, "Запрос подписки на маяк " + beaconId + " отправлен"),
                            error -> Log.e(TAG, "Ошибка подписки на маяк " + beaconId, error)
                    );
        } catch (Exception e) {
            Log.e(TAG, "Исключение при подписке на маяк", e);
        }
    }

    /**
     * Отписаться от обновлений конкретного маяка
     */
    public void unsubscribeFromBeacon(int beaconId) {
        if (hubConnection == null || hubConnection.getState() != HubConnectionState.CONNECTED) {
            Log.w(TAG, "Не подключены к hub'у");
            return;
        }

        try {
            hubConnection.invoke("UnsubscribeFromBeacon", beaconId)
                    .subscribe(
                            () -> Log.d(TAG, "Запрос отписки от маяка " + beaconId + " отправлен"),
                            error -> Log.e(TAG, "Ошибка отписки от маяка " + beaconId, error)
                    );
        } catch (Exception e) {
            Log.e(TAG, "Исключение при отписке от маяка", e);
        }
    }

    /**
     * Запросить статус сервера
     */
    public void getServerStatus() {
        if (hubConnection == null || hubConnection.getState() != HubConnectionState.CONNECTED) {
            Log.w(TAG, "Не подключены к hub'у");
            return;
        }

        try {
            hubConnection.invoke("GetServerStatus")
                    .subscribe(
                            () -> Log.d(TAG, "Запрос статуса отправлен"),
                            error -> Log.e(TAG, "Ошибка запроса статуса", error)
                    );
        } catch (Exception e) {
            Log.e(TAG, "Исключение при запросе статуса", e);
        }
    }

    /**
     * Проверить статус подключения
     */
    public boolean isConnected() {
        return hubConnection != null && hubConnection.getState() == HubConnectionState.CONNECTED;
    }

    /**
     * Отключиться от Hub'а
     */
    public void disconnect() {
        if (hubConnection != null) {
            try {
                hubConnection.stop()
                        .subscribe(
                                () -> {
                                    Log.i(TAG, "Отключены от сервера");
                                    notifyDisconnected();
                                },
                                error -> Log.e(TAG, "Ошибка при отключении", error)
                        );
                hubConnection = null;
            } catch (Exception e) {
                Log.e(TAG, "Исключение при отключении", e);
            }
        }
    }

    /**
     * Добавить listener для обновлений позиций
     */
    public void addListener(PositionUpdateListener listener) {
        if (listener != null && !listeners.contains(listener)) {
            listeners.add(listener);
        }
    }

    /**
     * Удалить listener
     */
    public void removeListener(PositionUpdateListener listener) {
        listeners.remove(listener);
    }

    // === Helpers для уведомления listener'ов ===

    private void notifyPositionUpdate(MapPoint position) {
        for (PositionUpdateListener listener : listeners) {
            try {
                listener.onPositionUpdate(position);
            } catch (Exception e) {
                Log.e(TAG, "Ошибка в listener при обновлении позиции", e);
            }
        }
    }

    private void notifyConnected(String connectionId) {
        for (PositionUpdateListener listener : listeners) {
            try {
                listener.onConnected(connectionId);
            } catch (Exception e) {
                Log.e(TAG, "Ошибка в listener при подключении", e);
            }
        }
    }

    private void notifyDisconnected() {
        for (PositionUpdateListener listener : listeners) {
            try {
                listener.onDisconnected();
            } catch (Exception e) {
                Log.e(TAG, "Ошибка в listener при отключении", e);
            }
        }
    }

    private void notifyError(String message) {
        for (PositionUpdateListener listener : listeners) {
            try {
                listener.onError(message);
            } catch (Exception e) {
                Log.e(TAG, "Ошибка в listener при notification ошибки", e);
            }
        }
    }
}
