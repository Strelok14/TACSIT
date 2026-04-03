package com.example.tacsit;

import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.text.TextUtils;
import android.widget.Toast;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;

import com.example.tacsit.network.AuthApi;
import com.example.tacsit.network.AuthRequest;
import com.example.tacsit.network.AuthResponse;
import com.example.tacsit.network.AuthServiceFactory;
import com.example.tacsit.network.SessionManager;

import retrofit2.Call;
import retrofit2.Callback;
import retrofit2.Response;

public class MainActivity extends AppCompatActivity {

    private TextInputEditText ipEditText;
    private TextInputEditText loginEditText;
    private TextInputEditText passwordEditText;
    private String pendingServerIp;

    private final ActivityResultLauncher<String> locationPermissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestPermission(), granted -> {
                if (granted) {
                    startTrackingService();
                }
                openMenuScreen(pendingServerIp);
            });

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        ipEditText = findViewById(R.id.ipEditText);
        loginEditText = findViewById(R.id.loginEditText);
        passwordEditText = findViewById(R.id.passwordEditText);
        MaterialButton signInButton = findViewById(R.id.signInButton);

        signInButton.setOnClickListener(view -> {
            String ip = getText(ipEditText);
            String login = getText(loginEditText);
            String password = getText(passwordEditText);

            if (TextUtils.isEmpty(ip) || TextUtils.isEmpty(login) || TextUtils.isEmpty(password)) {
                Toast.makeText(this, getString(R.string.fill_all_fields), Toast.LENGTH_SHORT).show();
                return;
            }

            signInButton.setEnabled(false);
            authorize(ip, login, password, signInButton);
        });
    }

    private void authorize(String serverIp, String login, String password, MaterialButton signInButton) {
        AuthApi authApi;
        try {
            authApi = AuthServiceFactory.create(serverIp);
        } catch (IllegalArgumentException e) {
            Toast.makeText(this, getString(R.string.invalid_server_url), Toast.LENGTH_SHORT).show();
            signInButton.setEnabled(true);
            return;
        }

        authApi.login(new AuthRequest(login, password)).enqueue(new Callback<>() {
            @Override
            public void onResponse(Call<AuthResponse> call, Response<AuthResponse> response) {
                signInButton.setEnabled(true);

                if (!response.isSuccessful()) {
                    Toast.makeText(MainActivity.this, getString(R.string.auth_failed), Toast.LENGTH_SHORT).show();
                    return;
                }

                AuthResponse body = response.body();
                if (body != null && body.isSuccess()) {
                    pendingServerIp = serverIp;
                    SessionManager.setSession(body.getToken(), body.getRefreshToken(), body.getRole(), serverIp, body.getUserId(), body.getHmacKey());
                    requestLocationAndContinue();
                    return;
                }

                if (body != null && !TextUtils.isEmpty(body.getMessage())) {
                    Toast.makeText(MainActivity.this, body.getMessage(), Toast.LENGTH_SHORT).show();
                } else {
                    Toast.makeText(MainActivity.this, getString(R.string.auth_failed), Toast.LENGTH_SHORT).show();
                }
            }

            @Override
            public void onFailure(Call<AuthResponse> call, Throwable throwable) {
                signInButton.setEnabled(true);
                Toast.makeText(MainActivity.this, getString(R.string.network_error), Toast.LENGTH_SHORT).show();
            }
        });
    }

    private void openMenuScreen(String serverIp) {
        Intent intent = new Intent(this, MenuActivity.class);
        intent.putExtra(MenuActivity.EXTRA_SERVER_IP, serverIp);
        startActivity(intent);
    }

    private void requestLocationAndContinue() {
        if (ContextCompat.checkSelfPermission(this, android.Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) {
            startTrackingService();
            openMenuScreen(pendingServerIp);
            return;
        }

        locationPermissionLauncher.launch(android.Manifest.permission.ACCESS_FINE_LOCATION);
    }

    private void startTrackingService() {
        Intent serviceIntent = new Intent(this, com.example.tacsit.tracking.LocalTrackingService.class);
        serviceIntent.putExtra(com.example.tacsit.tracking.LocalTrackingService.EXTRA_SERVER_URL, pendingServerIp);
        ContextCompat.startForegroundService(this, serviceIntent);
    }

    private String getText(TextInputEditText editText) {
        if (editText.getText() == null) {
            return "";
        }
        return editText.getText().toString().trim();
    }
}

