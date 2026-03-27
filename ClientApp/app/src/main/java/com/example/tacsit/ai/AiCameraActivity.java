package com.example.tacsit.ai;

import android.Manifest;
import android.content.Context;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.text.TextUtils;
import android.view.View;
import android.widget.TextView;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.camera.view.PreviewView;
import androidx.core.content.ContextCompat;

import com.example.tacsit.MeasurementActivity;
import com.example.tacsit.R;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.webrtc.EglBase;
import org.webrtc.SessionDescription;
import org.webrtc.SurfaceViewRenderer;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class AiCameraActivity extends AppCompatActivity {

    private static final long OBSERVATION_UPLOAD_INTERVAL_MS = 1000L;

    private final ActivityResultLauncher<String> cameraPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), isGranted -> {
                if (isGranted) {
                    requestLocationPermissionIfNeeded();
                    startAiSession();
                    return;
                }
                updateStatus(getString(R.string.ai_status_camera_permission_denied));
            });

    private final ActivityResultLauncher<String> locationPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), this::onLocationPermissionResult);

    private SurfaceViewRenderer surfaceViewRenderer;
    private PreviewView previewView;
    private OverlayView overlayView;
    private TextInputEditText signalUrlEditText;
    private TextView statusTextView;

    private EglBase eglBase;
    private WebRtcClient webRtcClient;
    private SignalingClient signalingClient;
    private DevicePoseTracker poseTracker;
    private AiObservationUploader observationUploader;
    private OnDeviceAiController onDeviceAiController;
    private String deviceId;
    private boolean uploadWarningShown;
    private long lastObservationUploadAtMs;
    private String serverIp;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_ai_camera);

        surfaceViewRenderer = findViewById(R.id.aiSurfaceView);
        previewView = findViewById(R.id.aiPreviewView);
        previewView.setScaleType(PreviewView.ScaleType.FILL_CENTER);
        overlayView = findViewById(R.id.aiOverlayView);
        signalUrlEditText = findViewById(R.id.signalUrlEditText);
        statusTextView = findViewById(R.id.aiStatusTextView);
        MaterialButton connectButton = findViewById(R.id.connectAiButton);
        MaterialButton disconnectButton = findViewById(R.id.disconnectAiButton);

        eglBase = EglBase.create();
        surfaceViewRenderer.init(eglBase.getEglBaseContext(), null);
        surfaceViewRenderer.setEnableHardwareScaler(true);
        surfaceViewRenderer.setMirror(false);

        serverIp = getIntent().getStringExtra(MeasurementActivity.EXTRA_SERVER_IP);
        poseTracker = new DevicePoseTracker(this);
        deviceId = resolveDeviceId(this);

        if (!TextUtils.isEmpty(serverIp) && !"test".equalsIgnoreCase(serverIp.trim())) {
            try {
                observationUploader = new AiObservationUploader(serverIp, () -> runOnUiThread(() -> {
                    if (!uploadWarningShown) {
                        uploadWarningShown = true;
                        updateStatus(getString(R.string.ai_status_upload_failed));
                    }
                }));
            } catch (IllegalArgumentException exception) {
                updateStatus(getString(R.string.ai_status_upload_disabled));
            }
        }

        signalUrlEditText.setText(buildDefaultSignalUrl(serverIp));
        updateStatus(getString(R.string.ai_status_idle));

        onDeviceAiController = new OnDeviceAiController(this, new OnDeviceAiController.Listener() {
            @Override
            public void onDetections(int frameWidth, int frameHeight, List<AiDetection> detections) {
                runOnUiThread(() -> {
                    overlayView.updateDetections(frameWidth, frameHeight, detections);
                    maybeUploadObservations(frameWidth, frameHeight, detections);
                    updateStatus(getString(R.string.ai_status_local_streaming, detections.size()));
                });
            }

            @Override
            public void onError(String message, Throwable throwable) {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_error, describeError(message, throwable))));
            }
        });

        connectButton.setOnClickListener(view -> ensureCameraAndConnect());
        disconnectButton.setOnClickListener(view -> stopAiSession(getString(R.string.ai_status_disconnected)));
    }

    @Override
    protected void onDestroy() {
        stopAiSession(null);
        if (poseTracker != null) {
            poseTracker.stop();
        }
        if (onDeviceAiController != null) {
            onDeviceAiController.release();
        }
        if (surfaceViewRenderer != null) {
            surfaceViewRenderer.release();
        }
        if (eglBase != null) {
            eglBase.release();
        }
        super.onDestroy();
    }

    private void ensureCameraAndConnect() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            requestLocationPermissionIfNeeded();
            startAiSession();
            return;
        }
        cameraPermissionLauncher.launch(Manifest.permission.CAMERA);
    }

    private void startAiSession() {
        String signalingUrl = textOf(signalUrlEditText);

        if (isLocalMode(signalingUrl)) {
            startLocalAiSession();
            return;
        }

        if (TextUtils.isEmpty(signalingUrl) || !isValidWsUrl(signalingUrl)) {
            updateStatus(getString(R.string.ai_status_invalid_url));
            return;
        }

        stopAiSession(null);
        overlayView.clear();
        uploadWarningShown = false;
        updateStatus(getString(R.string.ai_status_connecting));
        setRemotePreviewMode();

        if (poseTracker != null) {
            poseTracker.start(this);
        }

        webRtcClient = new WebRtcClient(getApplicationContext(), eglBase, surfaceViewRenderer, new WebRtcClient.Listener() {
            @Override
            public void onLocalOfferReady(SessionDescription offer) {
                runOnUiThread(() -> {
                    updateStatus(getString(R.string.ai_status_offer_sent));
                    if (signalingClient != null) {
                        signalingClient.sendOffer(offer);
                    }
                });
            }

            @Override
            public void onDetectionsMessage(String message) {
                runOnUiThread(() -> renderDetections(message));
            }

            @Override
            public void onConnectionStateChanged(String state) {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_error, state)));
            }

            @Override
            public void onError(String message, Throwable throwable) {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_error, describeError(message, throwable))));
            }
        });

        signalingClient = new SignalingClient(signalingUrl, new SignalingClient.Listener() {
            @Override
            public void onConnected() {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_signaling_ready)));
                webRtcClient.createOffer();
            }

            @Override
            public void onAnswer(String sdp) {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_answer_applied)));
                webRtcClient.applyAnswer(sdp);
            }

            @Override
            public void onError(String message, Throwable throwable) {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_error, describeError(message, throwable))));
            }

            @Override
            public void onClosed() {
                runOnUiThread(() -> updateStatus(getString(R.string.ai_status_disconnected)));
            }
        });

        try {
            webRtcClient.start();
            signalingClient.connect();
        } catch (Throwable exception) {
            stopAiSession(null);
            updateStatus(getString(R.string.ai_status_error, describeError("Failed to start AI session", exception)));
        }
    }

    private void stopAiSession(String statusMessage) {
        if (onDeviceAiController != null) {
            onDeviceAiController.stop();
        }
        if (signalingClient != null) {
            signalingClient.close();
            signalingClient = null;
        }
        if (webRtcClient != null) {
            webRtcClient.release();
            webRtcClient = null;
        }
        if (poseTracker != null) {
            poseTracker.stop();
        }
        overlayView.clear();
        if (statusMessage != null) {
            updateStatus(statusMessage);
        }
    }

    private void startLocalAiSession() {
        stopAiSession(null);
        overlayView.clear();
        uploadWarningShown = false;
        setLocalPreviewMode();
        updateStatus(getString(R.string.ai_status_local_starting));

        if (poseTracker != null) {
            poseTracker.start(this);
        }
        if (onDeviceAiController != null) {
            onDeviceAiController.start(previewView);
        }
    }

    private void renderDetections(String payload) {
        try {
            JSONObject jsonObject = new JSONObject(payload);
            if (!"detections".equals(jsonObject.optString("type"))) {
                return;
            }

            int frameWidth = jsonObject.optInt("frame_w", 0);
            int frameHeight = jsonObject.optInt("frame_h", 0);
            JSONArray detectionsArray = jsonObject.optJSONArray("detections");
            List<AiDetection> detections = new ArrayList<>();
            if (detectionsArray != null) {
                for (int index = 0; index < detectionsArray.length(); index++) {
                    JSONObject item = detectionsArray.optJSONObject(index);
                    if (item == null) {
                        continue;
                    }
                    detections.add(new AiDetection(
                            item.optString("cls", "person"),
                            (float) item.optDouble("conf", 0.0),
                            (float) item.optDouble("x1", 0.0),
                            (float) item.optDouble("y1", 0.0),
                            (float) item.optDouble("x2", 0.0),
                            (float) item.optDouble("y2", 0.0)
                    ));
                }
            }
            overlayView.updateDetections(frameWidth, frameHeight, detections);

            maybeUploadObservations(frameWidth, frameHeight, detections);
            updateStatus(getString(R.string.ai_status_streaming, detections.size()));
        } catch (JSONException exception) {
            updateStatus(getString(R.string.ai_status_error, describeError("Failed to parse detections", exception)));
        }
    }

    private void maybeUploadObservations(int frameWidth, int frameHeight, List<AiDetection> detections) {
        if (observationUploader == null || poseTracker == null || detections.isEmpty()) {
            return;
        }

        long now = System.currentTimeMillis();
        if (now - lastObservationUploadAtMs < OBSERVATION_UPLOAD_INTERVAL_MS) {
            return;
        }
        lastObservationUploadAtMs = now;

        DevicePoseTracker.Snapshot snapshot = poseTracker.snapshot();
        List<AiObservation> observations = AiObservationEstimator.buildObservations(
                deviceId,
                snapshot,
                frameWidth,
                frameHeight,
                detections
        );
        observationUploader.send(observations);
    }

    private void requestLocationPermissionIfNeeded() {
        if (hasLocationPermission()) {
            return;
        }
        locationPermissionLauncher.launch(Manifest.permission.ACCESS_FINE_LOCATION);
    }

    private void onLocationPermissionResult(boolean isGranted) {
        if (isGranted && poseTracker != null) {
            poseTracker.start(this);
        }
    }

    private boolean hasLocationPermission() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
                || ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED;
    }

    private String resolveDeviceId(Context context) {
        String androidId = Settings.Secure.getString(context.getContentResolver(), Settings.Secure.ANDROID_ID);
        if (androidId == null || androidId.isBlank()) {
            return "unknown-device";
        }
        return androidId;
    }

    private String buildDefaultSignalUrl(String serverInput) {
        String host = extractHost(serverInput);
        if (host.isBlank()) {
            return "";
        }
        boolean isLan = "localhost".equalsIgnoreCase(host)
                || host.matches("^\\d{1,3}(\\.\\d{1,3}){3}$")
                || host.endsWith(".local");
        String scheme = isLan ? "ws" : "wss";
        return String.format(Locale.US, "%s://%s:8080/ws", scheme, host);
    }

    private String extractHost(String serverInput) {
        String raw = serverInput == null ? "" : serverInput.trim();
        if (raw.isBlank() || "test".equalsIgnoreCase(raw)) {
            return "";
        }

        String normalized = raw.startsWith("http://") || raw.startsWith("https://")
                ? raw
                : "https://" + raw;

        try {
            Uri uri = Uri.parse(normalized);
            String host = uri.getHost();
            if (host != null && !host.isBlank()) {
                return host;
            }
        } catch (RuntimeException ignored) {
        }

        int delimiter = raw.indexOf(':');
        if (delimiter > 0) {
            return raw.substring(0, delimiter);
        }
        return raw;
    }

    private boolean isValidWsUrl(String url) {
        Uri uri = Uri.parse(url);
        String scheme = uri.getScheme();
        return uri.getHost() != null && ("ws".equalsIgnoreCase(scheme) || "wss".equalsIgnoreCase(scheme));
    }

    private boolean isLocalMode(String value) {
        String normalized = value == null ? "" : value.trim().toLowerCase(Locale.US);
        return "local".equals(normalized) || normalized.startsWith("local://");
    }

    private void setLocalPreviewMode() {
        surfaceViewRenderer.setVisibility(View.GONE);
        previewView.setVisibility(View.VISIBLE);
    }

    private void setRemotePreviewMode() {
        previewView.setVisibility(View.GONE);
        surfaceViewRenderer.setVisibility(View.VISIBLE);
    }

    private void updateStatus(@NonNull String message) {
        statusTextView.setText(message);
    }

    private String describeError(String message, Throwable throwable) {
        if (throwable == null || throwable.getMessage() == null || throwable.getMessage().isBlank()) {
            return message;
        }
        return message + ": " + throwable.getMessage();
    }

    private String textOf(TextInputEditText editText) {
        if (editText.getText() == null) {
            return "";
        }
        return editText.getText().toString().trim();
    }
}

