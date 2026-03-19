package com.example.tacsit.network;

import com.google.gson.annotations.SerializedName;

public class AuthResponse {

    @SerializedName("success")
    private boolean success;

    @SerializedName("token")
    private String token;

    @SerializedName("message")
    private String message;

    @SerializedName("role")
    private String role;

    @SerializedName("refreshToken")
    private String refreshToken;

    @SerializedName("refreshExpiresAtUtc")
    private String refreshExpiresAtUtc;

    public boolean isSuccess() {
        return success;
    }

    public String getToken() {
        return token;
    }

    public String getMessage() {
        return message;
    }

    public String getRole() {
        return role;
    }

    public String getRefreshToken() {
        return refreshToken;
    }

    public String getRefreshExpiresAtUtc() {
        return refreshExpiresAtUtc;
    }
}

