package com.example.tacsit.network;

import com.google.gson.annotations.SerializedName;

import java.util.List;

public class MeasurementRequest {

    @SerializedName("beaconId")
    private final int beaconId;

    @SerializedName("distances")
    private final List<DistanceItem> distances;

    @SerializedName("timestamp")
    private final long timestamp;

    @SerializedName("batteryLevel")
    private final Integer batteryLevel;

    public MeasurementRequest(int beaconId, List<DistanceItem> distances, long timestamp, Integer batteryLevel) {
        this.beaconId = beaconId;
        this.distances = distances;
        this.timestamp = timestamp;
        this.batteryLevel = batteryLevel;
    }

    public int getBeaconId() {
        return beaconId;
    }

    public List<DistanceItem> getDistances() {
        return distances;
    }

    public long getTimestamp() {
        return timestamp;
    }

    public Integer getBatteryLevel() {
        return batteryLevel;
    }

    public static class DistanceItem {

        @SerializedName("anchorId")
        private final int anchorId;

        @SerializedName("distance")
        private final double distance;

        @SerializedName("rssi")
        private final Integer rssi;

        public DistanceItem(int anchorId, double distance, Integer rssi) {
            this.anchorId = anchorId;
            this.distance = distance;
            this.rssi = rssi;
        }

        public int getAnchorId() {
            return anchorId;
        }

        public double getDistance() {
            return distance;
        }

        public Integer getRssi() {
            return rssi;
        }
    }
}
