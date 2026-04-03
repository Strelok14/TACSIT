package com.example.tacsit.network;

public final class GpsPositionPayload {
    public final double latitude;
    public final double longitude;
    public final Double altitude;
    public final Double accuracy;

    public GpsPositionPayload(double latitude, double longitude, Double altitude, Double accuracy) {
        this.latitude = latitude;
        this.longitude = longitude;
        this.altitude = altitude;
        this.accuracy = accuracy;
    }
}