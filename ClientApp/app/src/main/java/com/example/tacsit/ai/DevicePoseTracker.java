package com.example.tacsit.ai;

import android.Manifest;
import android.annotation.SuppressLint;
import android.content.Context;
import android.content.pm.PackageManager;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Bundle;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;

import java.util.Locale;

public final class DevicePoseTracker implements SensorEventListener, LocationListener {

    private final SensorManager sensorManager;
    private final LocationManager locationManager;

    private Sensor rotationSensor;

    private volatile float yawDeg;
    private volatile float pitchDeg;
    private volatile float rollDeg;

    private volatile Double latitude;
    private volatile Double longitude;
    private volatile Double altitudeM;

    public DevicePoseTracker(@NonNull Context context) {
        Context appContext = context.getApplicationContext();
        sensorManager = (SensorManager) appContext.getSystemService(Context.SENSOR_SERVICE);
        locationManager = (LocationManager) appContext.getSystemService(Context.LOCATION_SERVICE);
    }

    public void start(@NonNull Context context) {
        rotationSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR);
        if (rotationSensor != null) {
            sensorManager.registerListener(this, rotationSensor, SensorManager.SENSOR_DELAY_GAME);
        }
        startLocation(context);
    }

    public void stop() {
        sensorManager.unregisterListener(this);
        try {
            locationManager.removeUpdates(this);
        } catch (SecurityException ignored) {
        }
    }

    public Snapshot snapshot() {
        return new Snapshot(yawDeg, pitchDeg, rollDeg, latitude, longitude, altitudeM, System.currentTimeMillis());
    }

    @SuppressLint("MissingPermission")
    private void startLocation(Context context) {
        if (!hasLocationPermission(context)) {
            return;
        }

        try {
            Location lastGps = locationManager.getLastKnownLocation(LocationManager.GPS_PROVIDER);
            applyLocation(lastGps);
            Location lastNet = locationManager.getLastKnownLocation(LocationManager.NETWORK_PROVIDER);
            applyLocation(lastNet);

            if (locationManager.isProviderEnabled(LocationManager.GPS_PROVIDER)) {
                locationManager.requestLocationUpdates(LocationManager.GPS_PROVIDER, 1000L, 1f, this);
            }
            if (locationManager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) {
                locationManager.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 1500L, 2f, this);
            }
        } catch (RuntimeException ignored) {
        }
    }

    private boolean hasLocationPermission(Context context) {
        return ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
                || ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED;
    }

    @Override
    public void onSensorChanged(SensorEvent event) {
        if (event.sensor.getType() != Sensor.TYPE_ROTATION_VECTOR) {
            return;
        }

        float[] rotationMatrix = new float[9];
        SensorManager.getRotationMatrixFromVector(rotationMatrix, event.values);

        float[] orientation = new float[3];
        SensorManager.getOrientation(rotationMatrix, orientation);

        yawDeg = normalizeDegrees((float) Math.toDegrees(orientation[0]));
        pitchDeg = (float) Math.toDegrees(orientation[1]);
        rollDeg = (float) Math.toDegrees(orientation[2]);
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
        // No-op.
    }

    @Override
    public void onLocationChanged(@NonNull Location location) {
        applyLocation(location);
    }

    @Override
    public void onProviderEnabled(@NonNull String provider) {
        // No-op.
    }

    @Override
    public void onProviderDisabled(@NonNull String provider) {
        // No-op.
    }

    @Override
    public void onStatusChanged(String provider, int status, Bundle extras) {
        // Deprecated callback, intentionally left empty for backward compatibility.
    }

    private void applyLocation(Location location) {
        if (location == null) {
            return;
        }
        latitude = location.getLatitude();
        longitude = location.getLongitude();
        altitudeM = location.hasAltitude() ? location.getAltitude() : null;
    }

    private static float normalizeDegrees(float value) {
        float normalized = value % 360f;
        if (normalized < 0f) {
            normalized += 360f;
        }
        return normalized;
    }

    public static final class Snapshot {
        public final float yawDeg;
        public final float pitchDeg;
        public final float rollDeg;
        public final Double latitude;
        public final Double longitude;
        public final Double altitudeM;
        public final long timestampMs;

        Snapshot(float yawDeg, float pitchDeg, float rollDeg, Double latitude, Double longitude, Double altitudeM, long timestampMs) {
            this.yawDeg = yawDeg;
            this.pitchDeg = pitchDeg;
            this.rollDeg = rollDeg;
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitudeM = altitudeM;
            this.timestampMs = timestampMs;
        }

        @NonNull
        @Override
        public String toString() {
            return String.format(Locale.US, "pose(yaw=%.1f,pitch=%.1f,roll=%.1f,lat=%s,lon=%s)",
                    yawDeg, pitchDeg, rollDeg,
                    latitude == null ? "?" : latitude,
                    longitude == null ? "?" : longitude);
        }
    }
}
