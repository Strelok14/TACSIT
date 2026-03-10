package com.example.tacsit.network;

import com.google.gson.annotations.SerializedName;

public class MapPoint {

    @SerializedName(value = "Id", alternate = {"id"})
    private long id;

    @SerializedName(value = "BeaconId", alternate = {"beaconId"})
    private int beaconId;

    @SerializedName(value = "Y", alternate = {"y", "lat", "latitude"})
    private Double lat;

    @SerializedName(value = "X", alternate = {"x", "lng", "lon", "longitude"})
    private Double lng;

    @SerializedName(value = "Z", alternate = {"z"})
    private Double z;

    @SerializedName(value = "Confidence", alternate = {"confidence"})
    private Double confidence;

    @SerializedName(value = "Method", alternate = {"method"})
    private String method;

    @SerializedName(value = "Timestamp", alternate = {"timestamp"})
    private String timestamp;

    public long getId() {
        return id;
    }

    public int getBeaconId() {
        return beaconId;
    }

    public double getLat() {
        if (lat == null) {
            return 0d;
        }
        return lat;
    }

    public double getLng() {
        if (lng == null) {
            return 0d;
        }
        return lng;
    }

    public Double getZ() {
        return z;
    }

    public Double getConfidence() {
        return confidence;
    }

    public String getMethod() {
        return method;
    }

    public String getTimestamp() {
        return timestamp;
    }

    public boolean hasCoordinates() {
        return lat != null && lng != null;
    }
}
