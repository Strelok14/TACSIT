package com.example.tacsit.ai;

import androidx.annotation.NonNull;

import com.example.tacsit.network.AiTelemetryApi;
import com.example.tacsit.network.AuthServiceFactory;
import com.google.gson.JsonElement;

import java.util.List;

import retrofit2.Callback;
import retrofit2.Response;
import retrofit2.Retrofit;

public final class AiObservationUploader {

    public interface Listener {
        void onRejected();
    }

    private final AiTelemetryApi api;
    private final Listener listener;

    public AiObservationUploader(@NonNull String serverInput, Listener listener) {
        Retrofit retrofit = AuthServiceFactory.createRetrofit(serverInput);
        this.api = retrofit.create(AiTelemetryApi.class);
        this.listener = listener;
    }

    public void send(@NonNull List<AiObservation> observations) {
        if (observations.isEmpty()) {
            return;
        }

        api.sendObservations(new AiObservationBatchRequest(observations)).enqueue(new Callback<>() {
            @Override
            public void onResponse(retrofit2.Call<JsonElement> call, Response<JsonElement> response) {
                if (!response.isSuccessful() && listener != null) {
                    listener.onRejected();
                }
            }

            @Override
            public void onFailure(retrofit2.Call<JsonElement> call, Throwable throwable) {
                if (listener != null) {
                    listener.onRejected();
                }
            }
        });
    }
}
