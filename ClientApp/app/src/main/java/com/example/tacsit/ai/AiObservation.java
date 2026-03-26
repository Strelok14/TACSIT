package com.example.tacsit.ai;

public final class AiObservation {

    public final String deviceId;
    public final long timestampMs;
    public final String classId;
    public final float confidence;
    public final float bboxCxPx;
    public final float bboxCyPx;
    public final float bboxWPx;
    public final float bboxHPx;
    public final int frameWidth;
    public final int frameHeight;
    public final float yawDeg;
    public final float pitchDeg;
    public final float rollDeg;
    public final float bearingDeg;
    public final float elevationDeg;
    public final Double rangeM;
    public final Double latitude;
    public final Double longitude;
    public final Double altitudeM;
    public final float quality;

    public AiObservation(
            String deviceId,
            long timestampMs,
            String classId,
            float confidence,
            float bboxCxPx,
            float bboxCyPx,
            float bboxWPx,
            float bboxHPx,
            int frameWidth,
            int frameHeight,
            float yawDeg,
            float pitchDeg,
            float rollDeg,
            float bearingDeg,
            float elevationDeg,
            Double rangeM,
            Double latitude,
            Double longitude,
            Double altitudeM,
            float quality
    ) {
        this.deviceId = deviceId;
        this.timestampMs = timestampMs;
        this.classId = classId;
        this.confidence = confidence;
        this.bboxCxPx = bboxCxPx;
        this.bboxCyPx = bboxCyPx;
        this.bboxWPx = bboxWPx;
        this.bboxHPx = bboxHPx;
        this.frameWidth = frameWidth;
        this.frameHeight = frameHeight;
        this.yawDeg = yawDeg;
        this.pitchDeg = pitchDeg;
        this.rollDeg = rollDeg;
        this.bearingDeg = bearingDeg;
        this.elevationDeg = elevationDeg;
        this.rangeM = rangeM;
        this.latitude = latitude;
        this.longitude = longitude;
        this.altitudeM = altitudeM;
        this.quality = quality;
    }
}
