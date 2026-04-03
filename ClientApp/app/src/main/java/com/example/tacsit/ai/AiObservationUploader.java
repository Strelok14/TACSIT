package com.example.tacsit.ai;

import androidx.annotation.NonNull;

import com.example.tacsit.network.DetectionUploadPayload;
import com.example.tacsit.network.SessionManager;
import com.example.tacsit.network.SignedPayloadDispatcher;

import java.util.List;
import java.util.stream.Collectors;

public final class AiObservationUploader {

    public interface Listener {
        void onRejected();
    }

    private final String serverUrl;
    private final Listener listener;

    public AiObservationUploader(@NonNull String serverInput, Listener listener) {
        this.serverUrl = serverInput;
        this.listener = listener;
    }

    public void send(@NonNull List<AiObservation> observations) {
        if (observations.isEmpty()) {
            return;
        }

        List<DetectionUploadPayload.DetectionPayload> payloads = observations.stream()
                .map(item -> new DetectionUploadPayload.DetectionPayload(
                        null,
                        "ally".equalsIgnoreCase(item.classId),
                        item.classId,
                        toSkeletonJson(item),
                        item.latitude,
                        item.longitude,
                        item.altitudeM,
                        item.rangeM
                ))
                .collect(Collectors.toList());

        boolean success = SignedPayloadDispatcher.postSignedJson(serverUrl, "api/detections", new DetectionUploadPayload(payloads));
        if (!success && listener != null) {
            listener.onRejected();
        }
    }

    private String toSkeletonJson(AiObservation observation) {
        return "{" +
                "\"deviceId\":\"" + observation.deviceId + "\"," +
                "\"timestampMs\":" + observation.timestampMs + "," +
                "\"frameWidth\":" + observation.frameWidth + "," +
                "\"frameHeight\":" + observation.frameHeight + "," +
                "\"bboxCxPx\":" + observation.bboxCxPx + "," +
                "\"bboxCyPx\":" + observation.bboxCyPx + "," +
                "\"bboxWPx\":" + observation.bboxWPx + "," +
                "\"bboxHPx\":" + observation.bboxHPx +
                "}";
    }
}
