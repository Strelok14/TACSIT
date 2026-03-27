package com.example.tacsit.ai;

public final class PoseKeypoint {

    private final int type;
    private final float x;
    private final float y;
    private final float score;

    public PoseKeypoint(int type, float x, float y, float score) {
        this.type = type;
        this.x = x;
        this.y = y;
        this.score = score;
    }

    public int getType() {
        return type;
    }

    public float getX() {
        return x;
    }

    public float getY() {
        return y;
    }

    public float getScore() {
        return score;
    }
}
