package com.example.tacsit.network;

import com.google.gson.JsonElement;

import retrofit2.Call;
import retrofit2.http.Body;
import retrofit2.http.POST;

public interface TelemetryApi {

    @POST("api/telemetry/measurement")
    Call<JsonElement> sendMeasurement(@Body MeasurementRequest request);
}
