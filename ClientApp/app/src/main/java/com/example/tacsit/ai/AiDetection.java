package com.example.tacsit.ai;

import androidx.annotation.NonNull;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class AiDetection {

    private final String cls;
    private final float confidence;
    private final float x1;
    private final float y1;
    private final float x2;
    private final float y2;
    private final List<PoseKeypoint> keypoints;
    private final long trackId;

    public AiDetection(String cls, float confidence, float x1, float y1, float x2, float y2) {
        this(cls, confidence, x1, y1, x2, y2, Collections.emptyList(), -1L);
    }

    public AiDetection(
            String cls,
            float confidence,
            float x1,
            float y1,
            float x2,
            float y2,
            @NonNull List<PoseKeypoint> keypoints
    ) {
        this(cls, confidence, x1, y1, x2, y2, keypoints, -1L);
    }

    public AiDetection(
            String cls,
            float confidence,
            float x1,
            float y1,
            float x2,
            float y2,
            @NonNull List<PoseKeypoint> keypoints,
            long trackId
    ) {
        this.cls = cls;
        this.confidence = confidence;
        this.x1 = x1;
        this.y1 = y1;
        this.x2 = x2;
        this.y2 = y2;
        this.keypoints = Collections.unmodifiableList(new ArrayList<>(keypoints));
        this.trackId = trackId;
    }

    public String getCls() {
        return cls;
    }

    public float getConfidence() {
        return confidence;
    }

    public float getX1() {
        return x1;
    }

    public float getY1() {
        return y1;
    }

    public float getX2() {
        return x2;
    }

    public float getY2() {
        return y2;
    }

    @NonNull
    public List<PoseKeypoint> getKeypoints() {
        return keypoints;
    }

    public long getTrackId() {
        return trackId;
    }
}