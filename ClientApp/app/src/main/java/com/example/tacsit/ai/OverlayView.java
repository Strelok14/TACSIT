package com.example.tacsit.ai;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.util.AttributeSet;
import android.view.View;

import androidx.annotation.Nullable;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class OverlayView extends View {

    private final Paint boxPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint labelBackgroundPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final List<AiDetection> detections = new ArrayList<>();
    private int frameWidth;
    private int frameHeight;

    public OverlayView(Context context) {
        this(context, null);
    }

    public OverlayView(Context context, @Nullable AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public OverlayView(Context context, @Nullable AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);

        boxPaint.setColor(0xFFFF6B35);
        boxPaint.setStyle(Paint.Style.STROKE);
        boxPaint.setStrokeWidth(6f);

        textPaint.setColor(0xFFFFFFFF);
        textPaint.setTextSize(34f);

        labelBackgroundPaint.setColor(0xCC111111);
        labelBackgroundPaint.setStyle(Paint.Style.FILL);
    }

    public void updateDetections(int frameWidth, int frameHeight, List<AiDetection> detections) {
        this.frameWidth = frameWidth;
        this.frameHeight = frameHeight;
        this.detections.clear();
        this.detections.addAll(detections);
        postInvalidateOnAnimation();
    }

    public void clear() {
        frameWidth = 0;
        frameHeight = 0;
        detections.clear();
        postInvalidateOnAnimation();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);

        if (frameWidth <= 0 || frameHeight <= 0 || detections.isEmpty()) {
            return;
        }

        float scaleX = (float) getWidth() / (float) frameWidth;
        float scaleY = (float) getHeight() / (float) frameHeight;

        for (AiDetection detection : detections) {
            RectF rect = new RectF(
                    detection.getX1() * scaleX,
                    detection.getY1() * scaleY,
                    detection.getX2() * scaleX,
                    detection.getY2() * scaleY
            );
            canvas.drawRect(rect, boxPaint);

            String label = String.format(
                    Locale.US,
                    "%s %.0f%%",
                    detection.getCls(),
                    detection.getConfidence() * 100f
            );
            float labelWidth = textPaint.measureText(label);
            float labelHeight = textPaint.getTextSize() + 18f;
            float labelTop = Math.max(0f, rect.top - labelHeight - 8f);
            RectF labelRect = new RectF(rect.left, labelTop, rect.left + labelWidth + 28f, labelTop + labelHeight);
            canvas.drawRoundRect(labelRect, 10f, 10f, labelBackgroundPaint);
            canvas.drawText(label, labelRect.left + 14f, labelRect.bottom - 12f, textPaint);
        }
    }
}