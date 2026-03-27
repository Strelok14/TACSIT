package com.example.tacsit.ai;

import androidx.annotation.NonNull;

import org.json.JSONException;
import org.json.JSONObject;
import org.webrtc.SessionDescription;

import java.util.concurrent.TimeUnit;

import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.WebSocket;
import okhttp3.WebSocketListener;

public final class SignalingClient {

    public interface Listener {
        void onConnected();

        void onAnswer(String sdp);

        void onError(String message, Throwable throwable);

        void onClosed();
    }

    private final String webSocketUrl;
    private final Listener listener;
    private final OkHttpClient client;

    private WebSocket webSocket;

    public SignalingClient(String webSocketUrl, Listener listener) {
        this.webSocketUrl = webSocketUrl;
        this.listener = listener;
        this.client = new OkHttpClient.Builder()
                .readTimeout(0, TimeUnit.MILLISECONDS)
                .build();
    }

    public void connect() {
        Request request = new Request.Builder()
                .url(webSocketUrl)
                .build();
        webSocket = client.newWebSocket(request, new ListenerAdapter());
    }

    public void sendOffer(SessionDescription offer) {
        if (webSocket == null) {
            listener.onError("WebSocket is not connected", null);
            return;
        }

        JSONObject payload = new JSONObject();
        try {
            payload.put("type", "offer");
            payload.put("sdp", offer.description);
            webSocket.send(payload.toString());
        } catch (JSONException exception) {
            listener.onError("Failed to serialize WebRTC offer", exception);
        }
    }

    public void close() {
        if (webSocket != null) {
            webSocket.close(1000, "client disconnect");
            webSocket = null;
        }
        client.dispatcher().executorService().shutdown();
    }

    private final class ListenerAdapter extends WebSocketListener {

        @Override
        public void onOpen(@NonNull WebSocket webSocket, @NonNull Response response) {
            listener.onConnected();
        }

        @Override
        public void onMessage(@NonNull WebSocket webSocket, @NonNull String text) {
            try {
                JSONObject jsonObject = new JSONObject(text);
                String type = jsonObject.optString("type");
                if ("answer".equals(type)) {
                    listener.onAnswer(jsonObject.optString("sdp"));
                    return;
                }
                if ("error".equals(type)) {
                    listener.onError(jsonObject.optString("message", "Server error"), null);
                }
            } catch (JSONException exception) {
                listener.onError("Failed to parse signaling message", exception);
            }
        }

        @Override
        public void onFailure(@NonNull WebSocket webSocket, @NonNull Throwable throwable, Response response) {
            listener.onError("WebSocket failure", throwable);
        }

        @Override
        public void onClosed(@NonNull WebSocket webSocket, int code, @NonNull String reason) {
            listener.onClosed();
        }
    }
}