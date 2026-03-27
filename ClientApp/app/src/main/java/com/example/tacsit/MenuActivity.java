package com.example.tacsit;

import android.content.Intent;
import android.os.Bundle;

import androidx.appcompat.app.AppCompatActivity;

import com.google.android.material.button.MaterialButton;

public class MenuActivity extends AppCompatActivity {

    public static final String EXTRA_SERVER_IP = "extra_server_ip";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_menu);

        String serverIp = getIntent().getStringExtra(EXTRA_SERVER_IP);
        MaterialButton mapButton = findViewById(R.id.openMapButton);
        MaterialButton measurementButton = findViewById(R.id.openMeasurementButton);
        MaterialButton aiCameraButton = findViewById(R.id.openAiCameraButton);
        mapButton.setOnClickListener(view -> {
            Intent intent = new Intent(this, MapActivity.class);
            intent.putExtra(MapActivity.EXTRA_SERVER_IP, serverIp);
            startActivity(intent);
        });

        measurementButton.setOnClickListener(view -> {
            Intent intent = new Intent(this, MeasurementActivity.class);
            intent.putExtra(MeasurementActivity.EXTRA_SERVER_IP, serverIp);
            startActivity(intent);
        });

        aiCameraButton.setOnClickListener(view -> {
            Intent intent = new Intent(this, com.example.tacsit.ai.AiCameraActivity.class);
            intent.putExtra(MeasurementActivity.EXTRA_SERVER_IP, serverIp);
            startActivity(intent);
        });
    }
}
