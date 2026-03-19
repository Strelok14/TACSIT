package com.example.tacsit.network;

import retrofit2.Call;
import retrofit2.http.Body;
import retrofit2.http.POST;

public interface AuthApi {

    @POST("api/auth/login")
    Call<AuthResponse> login(@Body AuthRequest request);

    @POST("api/auth/refresh")
    Call<AuthResponse> refresh(@Body RefreshRequest request);

    @POST("api/auth/logout")
    Call<Void> logout(@Body LogoutRequest request);
}

