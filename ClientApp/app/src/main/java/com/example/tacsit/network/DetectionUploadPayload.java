package com.example.tacsit.network;

import java.util.List;

public final class DetectionUploadPayload {
    public final List<DetectionPayload> detections;

    public DetectionUploadPayload(List<DetectionPayload> detections) {
        this.detections = detections;
    }

    public static final class DetectionPayload {
        public final Integer targetUserId;
        public final boolean isAlly;
        public final String label;
        public final String skeletonData;
        public final Double latitude;
        public final Double longitude;
        public final Double altitude;
        public final Double accuracy;

        public DetectionPayload(Integer targetUserId, boolean isAlly, String label, String skeletonData, Double latitude, Double longitude, Double altitude, Double accuracy) {
            this.targetUserId = targetUserId;
            this.isAlly = isAlly;
            this.label = label;
            this.skeletonData = skeletonData;
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
            this.accuracy = accuracy;
        }
    }
}