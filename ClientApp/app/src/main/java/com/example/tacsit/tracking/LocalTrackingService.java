package com.example.tacsit.tracking;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Build;
import android.os.IBinder;

import androidx.annotation.Nullable;
import androidx.core.app.NotificationCompat;
import androidx.core.content.ContextCompat;

import com.example.tacsit.R;
import com.example.tacsit.network.GpsPositionPayload;
import com.example.tacsit.network.SessionManager;
import com.example.tacsit.network.SignedPayloadDispatcher;

public class LocalTrackingService extends Service implements LocationListener {

    public static final String EXTRA_SERVER_URL = "extra_server_url";

    private static final String CHANNEL_ID = "tacid_tracking";
    private static final int NOTIFICATION_ID = 1101;
    private static final long GPS_INTERVAL_MS = 1000L;
    private static final float GPS_DISTANCE_M = 0f;

    private LocationManager locationManager;
    private String serverUrl;

    @Override
    public void onCreate() {
        super.onCreate();
        locationManager = (LocationManager) getSystemService(LOCATION_SERVICE);
        ensureNotificationChannel();
        startForeground(NOTIFICATION_ID, buildNotification("GPS-трекинг активен"));
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        serverUrl = intent != null ? intent.getStringExtra(EXTRA_SERVER_URL) : SessionManager.getServerUrl();
        requestLocationUpdates();
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        if (locationManager != null) {
            locationManager.removeUpdates(this);
        }
        super.onDestroy();
    }

    @Nullable
    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onLocationChanged(Location location) {
        if (serverUrl == null || location == null) {
            return;
        }

        SignedPayloadDispatcher.postSignedJson(serverUrl, "api/gps", new GpsPositionPayload(
                location.getLatitude(),
                location.getLongitude(),
                location.hasAltitude() ? location.getAltitude() : null,
                location.hasAccuracy() ? (double) location.getAccuracy() : null));
    }

    private void requestLocationUpdates() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
            stopSelf();
            return;
        }

        if (locationManager == null) {
            return;
        }

        if (locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER)) {
            locationManager.requestLocationUpdates(LocationManager.GPS_PROVIDER, GPS_INTERVAL_MS, GPS_DISTANCE_M, this);
        }

        if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
            locationManager.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, GPS_INTERVAL_MS, GPS_DISTANCE_M, this);
        }
    }

    private void ensureNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }

        NotificationChannel channel = new NotificationChannel(CHANNEL_ID, "T.A.C.I.D. GPS", NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("Передача GPS-координат в локальной сети");
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.createNotificationChannel(channel);
        }
    }

    private Notification buildNotification(String text) {
        return new NotificationCompat.Builder(this, CHANNEL_ID)
                .setContentTitle("T.A.C.I.D. tracking")
                .setContentText(text)
                .setSmallIcon(R.mipmap.ic_launcher)
                .setOngoing(true)
                .build();
    }
}