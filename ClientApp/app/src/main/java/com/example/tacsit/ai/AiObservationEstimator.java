package com.example.tacsit.ai;

import androidx.annotation.NonNull;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class AiObservationEstimator {

    private static final float DEFAULT_HORIZONTAL_FOV_DEG = 60f;
    private static final float ASSUMED_PERSON_HEIGHT_M = 1.70f;

    private AiObservationEstimator() {
    }

    @NonNull
    public static List<AiObservation> buildObservations(
            @NonNull String deviceId,
            @NonNull DevicePoseTracker.Snapshot pose,
            int frameWidth,
            int frameHeight,
            @NonNull List<AiDetection> detections
    ) {
        List<AiObservation> result = new ArrayList<>();
        if (frameWidth <= 0 || frameHeight <= 0 || detections.isEmpty()) {
            return result;
        }

        float cx = frameWidth / 2f;
        float cy = frameHeight / 2f;
        float fx = estimateFx(frameWidth);
        float fy = estimateFy(frameWidth, frameHeight, fx);

        for (AiDetection detection : detections) {
            float bboxW = Math.max(0f, detection.getX2() - detection.getX1());
            float bboxH = Math.max(0f, detection.getY2() - detection.getY1());
            if (bboxW < 1f || bboxH < 1f) {
                continue;
            }

            float bboxCx = (detection.getX1() + detection.getX2()) / 2f;
            float bboxCy = (detection.getY1() + detection.getY2()) / 2f;

            float angleXDeg = (float) Math.toDegrees(Math.atan((bboxCx - cx) / fx));
            float angleYDeg = (float) -Math.toDegrees(Math.atan((bboxCy - cy) / fy));

            float bearingDeg = normalizeDegrees(pose.yawDeg + angleXDeg);
            float elevationDeg = clamp(pose.pitchDeg + angleYDeg, -89.9f, 89.9f);

            Double rangeM = estimateRangeMeters(detection.getCls(), bboxH, fy);
            float quality = estimateQuality(detection.getConfidence(), pose, rangeM);

            result.add(new AiObservation(
                    deviceId,
                    pose.timestampMs,
                    detection.getCls(),
                    detection.getConfidence(),
                    bboxCx,
                    bboxCy,
                    bboxW,
                    bboxH,
                    frameWidth,
                    frameHeight,
                    pose.yawDeg,
                    pose.pitchDeg,
                    pose.rollDeg,
                    bearingDeg,
                    elevationDeg,
                    rangeM,
                    pose.latitude,
                    pose.longitude,
                    pose.altitudeM,
                    quality
            ));
        }
        return result;
    }

    private static float estimateFx(int frameWidth) {
        double halfFovRad = Math.toRadians(DEFAULT_HORIZONTAL_FOV_DEG / 2.0);
        return (float) (frameWidth / (2.0 * Math.tan(halfFovRad)));
    }

    private static float estimateFy(int frameWidth, int frameHeight, float fx) {
        float aspect = frameHeight == 0 ? 1f : (float) frameHeight / (float) frameWidth;
        return fx * aspect;
    }

    private static Double estimateRangeMeters(String cls, float bboxHeightPx, float fy) {
        if (bboxHeightPx <= 1f) {
            return null;
        }
        if (!"person".equalsIgnoreCase(cls)) {
            return null;
        }
        return (double) ((fy * ASSUMED_PERSON_HEIGHT_M) / bboxHeightPx);
    }

    private static float estimateQuality(float confidence, DevicePoseTracker.Snapshot pose, Double rangeM) {
        float base = clamp(confidence, 0f, 1f);
        float locationFactor = pose.latitude == null || pose.longitude == null ? 0.85f : 1f;
        float rangeFactor;
        if (rangeM == null) {
            rangeFactor = 0.9f;
        } else if (rangeM < 2d || rangeM > 150d) {
            rangeFactor = 0.75f;
        } else {
            rangeFactor = 1f;
        }
        return clamp(base * locationFactor * rangeFactor, 0f, 1f);
    }

    private static float normalizeDegrees(float value) {
        float normalized = value % 360f;
        if (normalized < 0f) {
            normalized += 360f;
        }
        return normalized;
    }

    private static float clamp(float value, float min, float max) {
        return Math.max(min, Math.min(max, value));
    }
}
