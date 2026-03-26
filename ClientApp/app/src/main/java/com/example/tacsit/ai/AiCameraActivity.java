package com.example.tacsit.ai;

import android.Manifest;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.text.TextUtils;
import android.widget.TextView;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
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

    private final ActivityResultLauncher<String> cameraPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), isGranted -> {
                if (isGranted) {
                    startAiSession();
                    return;
                }
                updateStatus(getString(R.string.ai_status_camera_permission_denied));
            });

    private SurfaceViewRenderer surfaceViewRenderer;
    private OverlayView overlayView;
    private TextInputEditText signalUrlEditText;
    private TextView statusTextView;

    private EglBase eglBase;
    private WebRtcClient webRtcClient;
    private SignalingClient signalingClient;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_ai_camera);

        surfaceViewRenderer = findViewById(R.id.aiSurfaceView);
        overlayView = findViewById(R.id.aiOverlayView);
        signalUrlEditText = findViewById(R.id.signalUrlEditText);
        statusTextView = findViewById(R.id.aiStatusTextView);
        MaterialButton connectButton = findViewById(R.id.connectAiButton);
        MaterialButton disconnectButton = findViewById(R.id.disconnectAiButton);

        eglBase = EglBase.create();
        surfaceViewRenderer.init(eglBase.getEglBaseContext(), null);
        surfaceViewRenderer.setEnableHardwareScaler(true);
        surfaceViewRenderer.setMirror(false);

        String serverIp = getIntent().getStringExtra(MeasurementActivity.EXTRA_SERVER_IP);
        signalUrlEditText.setText(buildDefaultSignalUrl(serverIp));
        updateStatus(getString(R.string.ai_status_idle));

        connectButton.setOnClickListener(view -> ensureCameraAndConnect());
        disconnectButton.setOnClickListener(view -> stopAiSession(getString(R.string.ai_status_disconnected)));
    }

    @Override
    protected void onDestroy() {
        stopAiSession(null);
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
            startAiSession();
            return;
        }
        cameraPermissionLauncher.launch(Manifest.permission.CAMERA);
    }

    private void startAiSession() {
        String signalingUrl = textOf(signalUrlEditText);
        if (TextUtils.isEmpty(signalingUrl) || !isValidWsUrl(signalingUrl)) {
            updateStatus(getString(R.string.ai_status_invalid_url));
            return;
        }

        stopAiSession(null);
        overlayView.clear();
        updateStatus(getString(R.string.ai_status_connecting));

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
        if (signalingClient != null) {
            signalingClient.close();
            signalingClient = null;
        }
        if (webRtcClient != null) {
            webRtcClient.release();
            webRtcClient = null;
        }
        overlayView.clear();
        if (statusMessage != null) {
            updateStatus(statusMessage);
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
            updateStatus(getString(R.string.ai_status_streaming, detections.size()));
        } catch (JSONException exception) {
            updateStatus(getString(R.string.ai_status_error, describeError("Failed to parse detections", exception)));
        }
    }

    private String buildDefaultSignalUrl(String serverInput) {
        String host = extractHost(serverInput);
        return String.format(Locale.US, "ws://%s:8080/ws", host);
    }

    private String extractHost(String serverInput) {
        String raw = serverInput == null ? "" : serverInput.trim();
        if (raw.isBlank() || "test".equalsIgnoreCase(raw)) {
            return "192.168.0.10";
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