package com.example.tacsit;

import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;

import com.example.tacsit.network.AuthServiceFactory;
import com.example.tacsit.network.MeasurementRequest;
import com.example.tacsit.network.TelemetryApi;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import retrofit2.Call;
import retrofit2.Callback;
import retrofit2.Response;
import retrofit2.Retrofit;

public class MeasurementActivity extends AppCompatActivity {

    public static final String EXTRA_SERVER_IP = "extra_server_ip";

    private TextInputEditText beaconIdEditText;
    private TextInputEditText anchorIdEditText;
    private TextInputEditText distanceEditText;
    private TextInputEditText rssiEditText;
    private TextInputEditText timestampEditText;
    private TextInputEditText batteryLevelEditText;
    private TextInputEditText intervalMsEditText;
    private TextView statusText;
    private MaterialButton sendButton;
    private MaterialButton toggleAutoSendButton;
    private MaterialButton preset500Button;
    private MaterialButton preset1000Button;
    private MaterialButton preset2000Button;
    private MaterialButton preset3000Button;

    private TelemetryApi telemetryApi;
    private final Handler autoSendHandler = new Handler(Looper.getMainLooper());
    private boolean autoSendEnabled;
    private boolean requestInFlight;

    private final Runnable autoSendRunnable = new Runnable() {
        @Override
        public void run() {
            if (!autoSendEnabled) {
                return;
            }
            if (!requestInFlight) {
                sendMeasurement(sendButton, false);
            }
            autoSendHandler.postDelayed(this, resolveIntervalMs());
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_measurement);

        beaconIdEditText = findViewById(R.id.beaconIdEditText);
        anchorIdEditText = findViewById(R.id.anchorIdEditText);
        distanceEditText = findViewById(R.id.distanceEditText);
        rssiEditText = findViewById(R.id.rssiEditText);
        timestampEditText = findViewById(R.id.timestampEditText);
        batteryLevelEditText = findViewById(R.id.batteryLevelEditText);
        intervalMsEditText = findViewById(R.id.intervalMsEditText);
        statusText = findViewById(R.id.measurementStatusText);
        sendButton = findViewById(R.id.sendMeasurementButton);
        toggleAutoSendButton = findViewById(R.id.toggleAutoSendButton);
        preset500Button = findViewById(R.id.preset500Button);
        preset1000Button = findViewById(R.id.preset1000Button);
        preset2000Button = findViewById(R.id.preset2000Button);
        preset3000Button = findViewById(R.id.preset3000Button);

        String serverIp = getIntent().getStringExtra(EXTRA_SERVER_IP);
        if (TextUtils.isEmpty(serverIp) || "test".equalsIgnoreCase(serverIp.trim())) {
            statusText.setText(getString(R.string.server_not_configured));
            sendButton.setEnabled(false);
            toggleAutoSendButton.setEnabled(false);
            return;
        }

        try {
            Retrofit retrofit = AuthServiceFactory.createRetrofit(serverIp);
            telemetryApi = retrofit.create(TelemetryApi.class);
            statusText.setText(getString(R.string.measurement_status_ready));
        } catch (IllegalArgumentException e) {
            statusText.setText(getString(R.string.invalid_server_url));
            sendButton.setEnabled(false);
            toggleAutoSendButton.setEnabled(false);
            return;
        }

        sendButton.setOnClickListener(view -> sendMeasurement(sendButton, true));
        toggleAutoSendButton.setOnClickListener(view -> toggleAutoSend());
        preset500Button.setOnClickListener(view -> applyIntervalPreset(500));
        preset1000Button.setOnClickListener(view -> applyIntervalPreset(1000));
        preset2000Button.setOnClickListener(view -> applyIntervalPreset(2000));
        preset3000Button.setOnClickListener(view -> applyIntervalPreset(3000));
        applyIntervalPreset(1000);
    }

    @Override
    protected void onPause() {
        autoSendHandler.removeCallbacks(autoSendRunnable);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        autoSendEnabled = false;
        autoSendHandler.removeCallbacks(autoSendRunnable);
        super.onDestroy();
    }

    private void toggleAutoSend() {
        if (autoSendEnabled) {
            autoSendEnabled = false;
            autoSendHandler.removeCallbacks(autoSendRunnable);
            toggleAutoSendButton.setText(getString(R.string.start_auto_send));
            statusText.setText(getString(R.string.measurement_status_auto_stopped));
            return;
        }

        int intervalMs = resolveIntervalMs();
        if (intervalMs < 200) {
            statusText.setText(getString(R.string.measurement_status_interval_invalid));
            return;
        }

        autoSendEnabled = true;
        toggleAutoSendButton.setText(getString(R.string.stop_auto_send));
        statusText.setText(getString(R.string.measurement_status_auto_running, intervalMs));
        autoSendHandler.removeCallbacks(autoSendRunnable);
        autoSendHandler.post(autoSendRunnable);
    }

    private int resolveIntervalMs() {
        String raw = textOf(intervalMsEditText);
        if (TextUtils.isEmpty(raw)) {
            return 1000;
        }
        try {
            return Integer.parseInt(raw);
        } catch (NumberFormatException ex) {
            return 1000;
        }
    }

    private void applyIntervalPreset(int intervalMs) {
        intervalMsEditText.setText(String.valueOf(intervalMs));
        updatePresetButtons(intervalMs);
        statusText.setText(getString(R.string.measurement_status_preset_selected, intervalMs));
    }

    private void updatePresetButtons(int selectedIntervalMs) {
        List<MaterialButton> presetButtons = new ArrayList<>();
        presetButtons.add(preset500Button);
        presetButtons.add(preset1000Button);
        presetButtons.add(preset2000Button);
        presetButtons.add(preset3000Button);

        int[] intervals = {500, 1000, 2000, 3000};
        for (int index = 0; index < presetButtons.size(); index++) {
            MaterialButton button = presetButtons.get(index);
            if (button == null) {
                continue;
            }
            boolean selected = intervals[index] == selectedIntervalMs;
            button.setAlpha(selected ? 1.0f : 0.65f);
        }
    }

    private void sendMeasurement(MaterialButton sendButton, boolean showSendingStatus) {
        if (requestInFlight) {
            statusText.setText(getString(R.string.measurement_status_request_in_flight));
            return;
        }

        String beaconIdValue = textOf(beaconIdEditText);
        String anchorIdValue = textOf(anchorIdEditText);
        String distanceValue = textOf(distanceEditText);
        String rssiValue = textOf(rssiEditText);
        String timestampValue = textOf(timestampEditText);
        String batteryValue = textOf(batteryLevelEditText);

        if (TextUtils.isEmpty(beaconIdValue) || TextUtils.isEmpty(anchorIdValue) || TextUtils.isEmpty(distanceValue)) {
            statusText.setText(getString(R.string.measurement_status_fill_required));
            return;
        }

        final int beaconId;
        final int anchorId;
        final double distance;
        final Integer rssi;
        final Integer batteryLevel;
        final long timestamp;

        try {
            beaconId = Integer.parseInt(beaconIdValue);
            anchorId = Integer.parseInt(anchorIdValue);
            distance = Double.parseDouble(distanceValue.replace(',', '.'));
            rssi = TextUtils.isEmpty(rssiValue) ? null : Integer.parseInt(rssiValue);
            batteryLevel = TextUtils.isEmpty(batteryValue) ? null : Integer.parseInt(batteryValue);
            timestamp = TextUtils.isEmpty(timestampValue)
                ? System.currentTimeMillis()
                : Long.parseLong(timestampValue);
        } catch (NumberFormatException ex) {
            statusText.setText(getString(R.string.measurement_status_invalid_numbers));
            return;
        }

        MeasurementRequest.DistanceItem distanceItem =
            new MeasurementRequest.DistanceItem(anchorId, distance, rssi);
        MeasurementRequest request = new MeasurementRequest(
            beaconId,
            Collections.singletonList(distanceItem),
            timestamp,
            batteryLevel
        );
        if (showSendingStatus) {
            statusText.setText(getString(R.string.measurement_status_sending));
        }
        requestInFlight = true;
        updateActionButtons();

        telemetryApi.sendMeasurement(request).enqueue(new Callback<>() {
            @Override
            public void onResponse(Call<com.google.gson.JsonElement> call, Response<com.google.gson.JsonElement> response) {
                requestInFlight = false;
                updateActionButtons();
                if (response.isSuccessful()) {
                    statusText.setText(getString(R.string.measurement_status_success));
                } else if (response.code() == 429) {
                    stopAutoSendWithStatus(getString(R.string.measurement_status_rate_limited));
                } else if (response.code() == 401) {
                    stopAutoSendWithStatus(getString(R.string.measurement_status_unauthorized));
                } else if (response.code() == 400) {
                    statusText.setText(getString(R.string.measurement_status_bad_request));
                } else {
                    statusText.setText(getString(R.string.measurement_status_server_error_code, response.code()));
                }
            }

            @Override
            public void onFailure(Call<com.google.gson.JsonElement> call, Throwable throwable) {
                requestInFlight = false;
                updateActionButtons();
                if (autoSendEnabled) {
                    autoSendEnabled = false;
                    autoSendHandler.removeCallbacks(autoSendRunnable);
                    toggleAutoSendButton.setText(getString(R.string.start_auto_send));
                    statusText.setText(getString(
                        R.string.measurement_status_network_error_auto_paused,
                        networkReason(throwable),
                        networkDetails(throwable)
                    ));
                    updateActionButtons();
                    return;
                }
                statusText.setText(getString(
                        R.string.measurement_status_network_error_details,
                        networkReason(throwable),
                        networkDetails(throwable)
                ));
            }
        });
    }

    private void stopAutoSendWithStatus(String message) {
        if (autoSendEnabled) {
            autoSendEnabled = false;
            autoSendHandler.removeCallbacks(autoSendRunnable);
            toggleAutoSendButton.setText(getString(R.string.start_auto_send));
        }
        statusText.setText(message);
        updateActionButtons();
    }

    private void updateActionButtons() {
        sendButton.setEnabled(!requestInFlight && telemetryApi != null);
        toggleAutoSendButton.setEnabled(telemetryApi != null);
    }

    private String networkReason(Throwable throwable) {
        if (throwable == null) {
            return "Unknown";
        }
        return throwable.getClass().getSimpleName();
    }

    private String networkDetails(Throwable throwable) {
        if (throwable == null || throwable.getMessage() == null || throwable.getMessage().isBlank()) {
            return "no details";
        }
        return throwable.getMessage();
    }

    private String textOf(TextInputEditText editText) {
        if (editText.getText() == null) {
            return "";
        }
        return editText.getText().toString().trim();
    }
}
