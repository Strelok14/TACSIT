package com.example.tacsit;

import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
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

public class MapActivity extends AppCompatActivity {

    public static final String EXTRA_SERVER_IP = "extra_server_ip";

    private static final long POLL_INTERVAL_MS = 100;

    private MapView mapView;
    private TextView connectionStatusText;
    private MapApi mapApi;
    private final Handler pollingHandler = new Handler(Looper.getMainLooper());
    private final List<Marker> markers = new ArrayList<>();
    private final Gson gson = new Gson();
    private boolean isConnected;
    private boolean requestInFlight;

    private final Runnable pollingRunnable = new Runnable() {
        @Override
        public void run() {
            if (!isConnected || mapApi == null) {
                return;
            }
            pollCoordinates();
            pollingHandler.postDelayed(this, POLL_INTERVAL_MS);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_map);

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
            updateStatus(R.string.status_connecting);
        } catch (IllegalArgumentException e) {
            isConnected = false;
            updateStatus(R.string.status_server_error);
            Toast.makeText(this, getString(R.string.invalid_server_url), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        mapView.onResume();
        if (isConnected) {
            pollingHandler.post(pollingRunnable);
        }
    }

    @Override
    protected void onPause() {
        pollingHandler.removeCallbacks(pollingRunnable);
        mapView.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        pollingHandler.removeCallbacks(pollingRunnable);
        mapView.onDetach();
        super.onDestroy();
    }

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

    private void renderPoints(List<MapPoint> points) {
        for (Marker marker : markers) {
            mapView.getOverlays().remove(marker);
        }
        markers.clear();

        for (MapPoint point : points) {
            Marker marker = new Marker(mapView);
            marker.setPosition(new GeoPoint(point.getLat(), point.getLng()));
            marker.setAnchor(Marker.ANCHOR_CENTER, Marker.ANCHOR_BOTTOM);
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
