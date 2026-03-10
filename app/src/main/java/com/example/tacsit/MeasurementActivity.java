package com.example.tacsit;

import android.os.Bundle;
import android.text.TextUtils;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;

import com.example.tacsit.network.AuthServiceFactory;
import com.example.tacsit.network.MeasurementRequest;
import com.example.tacsit.network.TelemetryApi;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;

import java.util.Collections;

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
    private TextView statusText;

    private TelemetryApi telemetryApi;

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
        statusText = findViewById(R.id.measurementStatusText);
        MaterialButton sendButton = findViewById(R.id.sendMeasurementButton);

        String serverIp = getIntent().getStringExtra(EXTRA_SERVER_IP);
        if (TextUtils.isEmpty(serverIp) || "test".equalsIgnoreCase(serverIp.trim())) {
            statusText.setText(getString(R.string.server_not_configured));
            sendButton.setEnabled(false);
            return;
        }

        try {
            Retrofit retrofit = AuthServiceFactory.createRetrofit(serverIp);
            telemetryApi = retrofit.create(TelemetryApi.class);
            statusText.setText(getString(R.string.measurement_status_ready));
        } catch (IllegalArgumentException e) {
            statusText.setText(getString(R.string.invalid_server_url));
            sendButton.setEnabled(false);
            return;
        }

        sendButton.setOnClickListener(view -> sendMeasurement(sendButton));
    }

    private void sendMeasurement(MaterialButton sendButton) {
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
        sendButton.setEnabled(false);
        statusText.setText(getString(R.string.measurement_status_sending));

        telemetryApi.sendMeasurement(request).enqueue(new Callback<>() {
            @Override
            public void onResponse(Call<com.google.gson.JsonElement> call, Response<com.google.gson.JsonElement> response) {
                sendButton.setEnabled(true);
                if (response.isSuccessful()) {
                    statusText.setText(getString(R.string.measurement_status_success));
                } else {
                    statusText.setText(getString(R.string.measurement_status_server_error));
                }
            }

            @Override
            public void onFailure(Call<com.google.gson.JsonElement> call, Throwable throwable) {
                sendButton.setEnabled(true);
                statusText.setText(getString(
                        R.string.measurement_status_network_error_details,
                        networkReason(throwable),
                        networkDetails(throwable)
                ));
            }
        });
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
