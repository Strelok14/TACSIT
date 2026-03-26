package com.example.tacsit;

import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.google.gson.Gson;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import com.example.tacsit.network.AuthServiceFactory;
import com.example.tacsit.network.MapApi;
import com.example.tacsit.network.MapPoint;
import com.example.tacsit.network.PositioningHubClient;
import com.example.tacsit.network.SessionManager;

import org.osmdroid.api.IMapController;
import org.osmdroid.config.Configuration;
import org.osmdroid.tileprovider.tilesource.TileSourceFactory;
import org.osmdroid.util.GeoPoint;
import org.osmdroid.views.MapView;
import org.osmdroid.views.overlay.Marker;

import java.util.ArrayList;
import java.util.List;

import retrofit2.Call;
import retrofit2.Callback;
import retrofit2.Response;
import retrofit2.Retrofit;

/**
 * MapActivity — показывает карту с позициями маяков в реальном времени
 * Использует SignalR WebSocket для efficient real-time обновлений,
 * с HTTP polling как fallback если WebSocket недоступен.
 */
public class MapActivity extends AppCompatActivity implements PositioningHubClient.PositionUpdateListener {

    public static final String EXTRA_SERVER_IP = "extra_server_ip";
    private static final String TAG = "MapActivity";

    private static final long POLL_INTERVAL_MS = 500;  // Fallback polling (менее частый)

    private MapView mapView;
    private TextView connectionStatusText;
    private MapApi mapApi;
    private PositioningHubClient hubClient;
    
    private final Handler pollingHandler = new Handler(Looper.getMainLooper());
    private final List<Marker> markers = new ArrayList<>();
    private final Gson gson = new Gson();
    
    private boolean isConnected;
    private boolean requestInFlight;
    private boolean useWebSocket = false;

    private final Runnable pollingRunnable = new Runnable() {
        @Override
        public void run() {
            if (!isConnected || mapApi == null || useWebSocket) {
                return;  // Не polling если есть WebSocket
            }
            pollCoordinates();
            pollingHandler.postDelayed(this, POLL_INTERVAL_MS);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_map);
        Log.d(TAG, "onCreate");

        Configuration.getInstance().setUserAgentValue(getPackageName());

        mapView = findViewById(R.id.mapView);
        connectionStatusText = findViewById(R.id.connectionStatusText);
        mapView.setTileSource(TileSourceFactory.MAPNIK);
        mapView.setMultiTouchControls(true);
        updateStatus(R.string.status_connecting);

        IMapController mapController = mapView.getController();
        mapController.setZoom(12.0);
        mapController.setCenter(new GeoPoint(55.751244, 37.618423));

        String serverIp = getIntent().getStringExtra(EXTRA_SERVER_IP);
        if (serverIp == null || serverIp.trim().isEmpty() || "test".equalsIgnoreCase(serverIp.trim())) {
            isConnected = false;
            updateStatus(R.string.status_server_not_configured);
            Toast.makeText(this, getString(R.string.server_not_configured), Toast.LENGTH_SHORT).show();
            return;
        }

        try {
            Retrofit retrofit = AuthServiceFactory.createRetrofit(serverIp);
            mapApi = retrofit.create(MapApi.class);
            isConnected = true;
            
            // Попытаемся подключиться через SignalR WebSocket
            String token = SessionManager.getAccessToken();
            if (token != null && !token.isEmpty()) {
                connectWebSocket(serverIp, token);
            } else {
                Log.w(TAG, "Токен не найден, используем HTTP polling");
                pollCoordinates();
            }
        } catch (IllegalArgumentException e) {
            isConnected = false;
            updateStatus(R.string.status_server_error);
            Toast.makeText(this, getString(R.string.invalid_server_url), Toast.LENGTH_SHORT).show();
        }
    }

    /**
     * Подключиться к SignalR Hub'у для real-time обновлений
     */
    private void connectWebSocket(String serverIp, String token) {
        Log.d(TAG, "Попытка подключения к SignalR Hub...");
        
        hubClient = new PositioningHubClient(serverIp, token);
        hubClient.addListener(this);

        hubClient.connect(
                () -> {
                    Log.i(TAG, "✓ SignalR подключен!");
                    useWebSocket = true;
                    updateStatus(R.string.status_connected_websocket);
                    
                    // Приостанавливаем HTTP polling если WebSocket успешен
                    pollingHandler.removeCallbacks(pollingRunnable);
                },
                () -> {
                    Log.w(TAG, "✗ SignalR подключение не удалось, переходим на HTTP polling");
                    useWebSocket = false;
                    updateStatus(R.string.status_no_websocket_polling);
                    
                    // Возвращаемся на HTTP polling как fallback
                    if (isConnected) {
                        pollingHandler.post(pollingRunnable);
                    }
                }
        );
    }

    @Override
    protected void onResume() {
        super.onResume();
        Log.d(TAG, "onResume");
        mapView.onResume();
        
        if (isConnected && !useWebSocket) {
            pollingHandler.post(pollingRunnable);
        }
    }

    @Override
    protected void onPause() {
        Log.d(TAG, "onPause");
        pollingHandler.removeCallbacks(pollingRunnable);
        mapView.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        Log.d(TAG, "onDestroy");
        pollingHandler.removeCallbacks(pollingRunnable);
        
        // Отключаемся от WebSocket
        if (hubClient != null) {
            hubClient.removeListener(this);
            hubClient.disconnect();
            hubClient = null;
        }
        
        mapView.onDetach();
        super.onDestroy();
    }

    /**
     * HTTP Polling как fallback если WebSocket недоступен
     */
    private void pollCoordinates() {
        if (requestInFlight || mapApi == null) {
            return;
        }
        requestInFlight = true;

        mapApi.getCoordinates().enqueue(new Callback<>() {
            @Override
            public void onResponse(Call<JsonElement> call, Response<JsonElement> response) {
                requestInFlight = false;
                if (!response.isSuccessful() || response.body() == null) {
                    updateStatus(R.string.status_server_error);
                    return;
                }
                List<MapPoint> points = parsePoints(response.body());
                if (points.isEmpty()) {
                    updateStatus(R.string.status_connected_no_data);
                } else {
                    updateStatus(R.string.status_connected);
                }
                renderPoints(points);
            }

            @Override
            public void onFailure(Call<JsonElement> call, Throwable throwable) {
                requestInFlight = false;
                updateStatusWithDetails(R.string.status_no_connection_details, throwable);
            }
        });
    }

    // === Implements PositionUpdateListener ===

    @Override
    public void onPositionUpdate(MapPoint position) {
        Log.d(TAG, "📍 Позиция получена через SignalR: " + position);
        runOnUiThread(() -> {
            List<MapPoint> points = new ArrayList<>();
            points.add(position);
            renderPoints(points);
            updateStatus(R.string.status_connected_websocket);
        });
    }

    @Override
    public void onConnected(String connectionId) {
        Log.d(TAG, "🔌 Подключены к Hub, connectionId: " + connectionId);
        runOnUiThread(() -> updateStatus(R.string.status_connected_websocket));
    }

    @Override
    public void onDisconnected() {
        Log.w(TAG, "🔌 Отключены от Hub");
        useWebSocket = false;
        runOnUiThread(() -> {
            updateStatus(R.string.status_no_websocket_polling);
            if (isConnected) {
                pollingHandler.post(pollingRunnable);
            }
        });
    }

    @Override
    public void onError(String message) {
        Log.e(TAG, "⚠️ Ошибка Hub: " + message);
        runOnUiThread(() -> Toast.makeText(this, message, Toast.LENGTH_LONG).show());
    }

    // === UI Update Methods ===

    private void updateStatus(int statusResId) {
        if (connectionStatusText != null) {
            connectionStatusText.setText(statusResId);
        }
    }

    private void updateStatusWithDetails(int statusResId, Throwable throwable) {
        if (connectionStatusText == null) {
            return;
        }

        String reason = throwable != null ? throwable.getClass().getSimpleName() : "Unknown";
        String details = throwable != null && throwable.getMessage() != null
                ? throwable.getMessage()
                : "no details";
        connectionStatusText.setText(getString(statusResId, reason, details));
    }

    // === Parsing Methods ===

    private List<MapPoint> parsePoints(JsonElement root) {
        List<MapPoint> points = new ArrayList<>();
        JsonArray rows = null;

        if (root == null || root.isJsonNull()) {
            return points;
        }

        if (root.isJsonArray()) {
            rows = root.getAsJsonArray();
        } else if (root.isJsonObject()) {
            JsonObject object = root.getAsJsonObject();
            rows = firstArray(object, "data", "items", "positions", "rows", "result");
        }

        if (rows == null) {
            return points;
        }

        for (JsonElement row : rows) {
            if (!row.isJsonObject()) {
                continue;
            }

            MapPoint point = gson.fromJson(row, MapPoint.class);
            if (point != null && point.hasCoordinates()) {
                points.add(point);
            }
        }

        return points;
    }

    private JsonArray firstArray(JsonObject object, String... keys) {
        for (String key : keys) {
            JsonElement value = object.get(key);
            if (value != null && value.isJsonArray()) {
                return value.getAsJsonArray();
            }
        }
        return null;
    }

    // === Rendering Methods ===

    private void renderPoints(List<MapPoint> points) {
        for (Marker marker : markers) {
            mapView.getOverlays().remove(marker);
        }
        markers.clear();

        for (MapPoint point : points) {
            Marker marker = new Marker(mapView);
            marker.setPosition(new GeoPoint(point.getLat(), point.getLng()));
            marker.setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_BOTTOM);
            marker.setTitle(point.getId() != null ? point.getId() : "Unknown");
            mapView.getOverlays().add(marker);
            markers.add(marker);
        }

        if (!points.isEmpty()) {
            GeoPoint firstPoint = new GeoPoint(points.get(0).getLat(), points.get(0).getLng());
            mapView.getController().setCenter(firstPoint);
        }
        mapView.invalidate();
    }
}
