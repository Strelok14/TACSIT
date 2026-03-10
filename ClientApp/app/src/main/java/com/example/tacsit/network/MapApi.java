package com.example.tacsit.network;

import com.google.gson.JsonElement;

import retrofit2.Call;
import retrofit2.http.GET;

public interface MapApi {

    @GET("api/positions")
    Call<JsonElement> getCoordinates();
}
