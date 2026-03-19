package com.example.tacsit.network;

import com.google.gson.annotations.SerializedName;

public class LogoutRequest {

    @SerializedName("refreshToken")
    private final String refreshToken;

    public LogoutRequest(String refreshToken) {
        this.refreshToken = refreshToken;
    }
}
