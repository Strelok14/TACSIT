package com.example.tacsit.network;

import com.example.tacsit.ai.AiObservationBatchRequest;
import com.google.gson.JsonElement;

import retrofit2.Call;
import retrofit2.http.Body;
import retrofit2.http.POST;

public interface AiTelemetryApi {

    @POST("api/telemetry/ai-observations")
    Call<JsonElement> sendObservations(@Body AiObservationBatchRequest request);
}
