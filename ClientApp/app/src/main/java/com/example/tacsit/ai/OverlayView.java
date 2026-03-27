package com.example.tacsit.ai;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.PointF;
import android.graphics.Paint;
import android.graphics.RectF;
import android.util.SparseArray;
import android.util.AttributeSet;
import android.view.View;

import androidx.annotation.Nullable;

import com.google.mlkit.vision.pose.PoseLandmark;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

public final class OverlayView extends View {

    private static final boolean SINGLE_PERSON_MODE = true;
    private static final float DEDUP_IOU_THRESHOLD = 0.58f;
    private static final float TRACK_MATCH_IOU_THRESHOLD = 0.22f;
    private static final float SMOOTH_ALPHA = 0.38f;
    private static final int MAX_MISSING_FRAMES = 1;
    private static final float ASSUMED_PERSON_HEIGHT_M = 1.70f;
    private static final float ASSUMED_CAMERA_FOV_DEG = 60f;

    private final Paint boxPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint labelBackgroundPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint skeletonLinePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint skeletonPointPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final List<AiDetection> detections = new ArrayList<>();
    private final Map<Long, TrackedPerson> trackedPeople = new HashMap<>();
    private long nextSyntheticTrackId = -1L;
    private long frameCounter = 0L;
    private int frameWidth;
    private int frameHeight;

    private static final int[][] SKELETON_CONNECTIONS = new int[][]{
            {PoseLandmark.LEFT_SHOULDER, PoseLandmark.RIGHT_SHOULDER},
            {PoseLandmark.LEFT_SHOULDER, PoseLandmark.LEFT_ELBOW},
            {PoseLandmark.LEFT_ELBOW, PoseLandmark.LEFT_WRIST},
            {PoseLandmark.RIGHT_SHOULDER, PoseLandmark.RIGHT_ELBOW},
            {PoseLandmark.RIGHT_ELBOW, PoseLandmark.RIGHT_WRIST},
            {PoseLandmark.LEFT_SHOULDER, PoseLandmark.LEFT_HIP},
            {PoseLandmark.RIGHT_SHOULDER, PoseLandmark.RIGHT_HIP},
            {PoseLandmark.LEFT_HIP, PoseLandmark.RIGHT_HIP},
            {PoseLandmark.LEFT_HIP, PoseLandmark.LEFT_KNEE},
            {PoseLandmark.LEFT_KNEE, PoseLandmark.LEFT_ANKLE},
            {PoseLandmark.RIGHT_HIP, PoseLandmark.RIGHT_KNEE},
            {PoseLandmark.RIGHT_KNEE, PoseLandmark.RIGHT_ANKLE}
        };

            private static final int[] PERSON_COLORS = new int[]{
                0xFFFF6B35,
                0xFF00C2A8,
                0xFFFFC145,
                0xFF4D9DE0,
                0xFFE15554,
                0xFF3BB273,
                0xFF7A77FF,
                0xFFEC9A29
            };

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

        skeletonLinePaint.setColor(0xFF19B4FF);
        skeletonLinePaint.setStyle(Paint.Style.STROKE);
        skeletonLinePaint.setStrokeWidth(5f);

        skeletonPointPaint.setColor(0xFF7FE7FF);
        skeletonPointPaint.setStyle(Paint.Style.FILL);
    }

    public void updateDetections(int frameWidth, int frameHeight, List<AiDetection> detections) {
        this.frameWidth = frameWidth;
        this.frameHeight = frameHeight;

        frameCounter++;
        for (TrackedPerson trackedPerson : trackedPeople.values()) {
            trackedPerson.matchedInThisFrame = false;
        }

        List<AiDetection> deduplicated = deduplicateDetections(detections);
        if (SINGLE_PERSON_MODE && !deduplicated.isEmpty()) {
            AiDetection primary = choosePrimaryDetection(deduplicated);
            deduplicated = Collections.singletonList(primary);
        }

        for (AiDetection incoming : deduplicated) {
            TrackedPerson track = matchTrack(incoming);
            if (track == null) {
                track = createTrack(incoming);
                trackedPeople.put(track.stableTrackId, track);
            }
            updateTrack(track, incoming);
        }

        List<Long> removeTrackIds = new ArrayList<>();
        for (TrackedPerson trackedPerson : trackedPeople.values()) {
            if (!trackedPerson.matchedInThisFrame) {
                trackedPerson.missingFrames++;
                if (trackedPerson.missingFrames > MAX_MISSING_FRAMES) {
                    removeTrackIds.add(trackedPerson.stableTrackId);
                }
            }
        }
        for (Long trackId : removeTrackIds) {
            trackedPeople.remove(trackId);
        }

        if (SINGLE_PERSON_MODE && trackedPeople.size() > 1) {
            TrackedPerson best = null;
            float bestScore = -1f;
            for (TrackedPerson trackedPerson : trackedPeople.values()) {
                if (trackedPerson.lastDetection == null) {
                    continue;
                }
                float score = trackedPerson.lastDetection.getConfidence() * 0.7f + area(trackedPerson.lastDetection) * 0.3f;
                if (score > bestScore) {
                    bestScore = score;
                    best = trackedPerson;
                }
            }
            if (best != null) {
                trackedPeople.clear();
                trackedPeople.put(best.stableTrackId, best);
            }
        }

        this.detections.clear();
        List<TrackedPerson> ordered = new ArrayList<>(trackedPeople.values());
        Collections.sort(ordered, Comparator.comparingInt(a -> a.missingFrames));
        for (TrackedPerson trackedPerson : ordered) {
            if (trackedPerson.lastDetection != null) {
                this.detections.add(trackedPerson.lastDetection);
            }
        }

        postInvalidateOnAnimation();
    }

    public void clear() {
        frameWidth = 0;
        frameHeight = 0;
        trackedPeople.clear();
        nextSyntheticTrackId = -1L;
        frameCounter = 0L;
        detections.clear();
        postInvalidateOnAnimation();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);

        if (frameWidth <= 0 || frameHeight <= 0 || detections.isEmpty()) {
            return;
        }

        float viewWidth = getWidth();
        float viewHeight = getHeight();
        float scale = Math.max(viewWidth / (float) frameWidth, viewHeight / (float) frameHeight);
        float dx = (viewWidth - frameWidth * scale) / 2f;
        float dy = (viewHeight - frameHeight * scale) / 2f;

        for (AiDetection detection : detections) {
            int color = resolvePersonColor(detection);
            boxPaint.setColor(color);
            skeletonLinePaint.setColor(color);
            skeletonPointPaint.setColor(adjustColor(color, 0.72f, 1.25f));
            labelBackgroundPaint.setColor(withAlpha(adjustColor(color, 0.55f, 0.9f), 0xD0));

            RectF rect = new RectF(
                    detection.getX1() * scale + dx,
                    detection.getY1() * scale + dy,
                    detection.getX2() * scale + dx,
                    detection.getY2() * scale + dy
            );
            canvas.drawRect(rect, boxPaint);

            drawSkeleton(canvas, detection, scale, dx, dy);

            String label = String.format(
                    Locale.US,
                    "%s %.0f%%",
                    detection.getCls(),
                    detection.getConfidence() * 100f
            );
            float distanceMeters = estimateDistanceMeters(detection);
            float yawDegrees = estimateYawDegrees(detection);
            String metrics = String.format(
                    Locale.US,
                    "%.1fm | %s",
                    distanceMeters,
                    formatDirection(yawDegrees)
            );

            float labelWidth = Math.max(textPaint.measureText(label), textPaint.measureText(metrics));
            float lineHeight = textPaint.getTextSize() + 6f;
            float labelHeight = lineHeight * 2f + 16f;
            float labelTop = Math.max(0f, rect.top - labelHeight - 8f);
            RectF labelRect = new RectF(rect.left, labelTop, rect.left + labelWidth + 28f, labelTop + labelHeight);
            canvas.drawRoundRect(labelRect, 10f, 10f, labelBackgroundPaint);

            float textX = labelRect.left + 14f;
            float firstLineY = labelRect.top + lineHeight;
            float secondLineY = firstLineY + lineHeight;
            canvas.drawText(label, textX, firstLineY, textPaint);
            canvas.drawText(metrics, textX, secondLineY, textPaint);
        }
    }

    private float estimateDistanceMeters(AiDetection detection) {
        float boxHeight = Math.max(1f, detection.getY2() - detection.getY1());
        float frameH = Math.max(1f, frameHeight);
        float fy = estimateFocalLengthPx(frameH);
        float distance = (ASSUMED_PERSON_HEIGHT_M * fy) / boxHeight;
        return clamp(distance, 0.5f, 30f);
    }

    private float estimateYawDegrees(AiDetection detection) {
        float frameW = Math.max(1f, frameWidth);
        float fx = estimateFocalLengthPx(frameW);
        float centerX = (detection.getX1() + detection.getX2()) * 0.5f;
        float offsetX = centerX - frameW * 0.5f;
        float radians = (float) Math.atan(offsetX / Math.max(1f, fx));
        return (float) Math.toDegrees(radians);
    }

    private float estimateFocalLengthPx(float frameSizePx) {
        float halfFovRad = (float) Math.toRadians(ASSUMED_CAMERA_FOV_DEG * 0.5f);
        return (float) (frameSizePx / (2f * Math.tan(halfFovRad)));
    }

    private String formatDirection(float yawDegrees) {
        float abs = Math.abs(yawDegrees);
        if (abs < 4f) {
            return "center";
        }
        return yawDegrees < 0f
                ? String.format(Locale.US, "left %.0fdeg", abs)
                : String.format(Locale.US, "right %.0fdeg", abs);
    }

    private int resolvePersonColor(AiDetection detection) {
        long trackId = detection.getTrackId();
        int colorIndex;
        if (trackId >= 0L) {
            colorIndex = (int) (Math.abs(trackId) % PERSON_COLORS.length);
        } else {
            int seed = (int) (Math.abs(detection.getX1()) * 31f + Math.abs(detection.getY1()) * 17f + Math.abs(detection.getX2()) * 13f);
            colorIndex = seed % PERSON_COLORS.length;
        }
        return PERSON_COLORS[colorIndex];
    }

    private int adjustColor(int color, float saturationScale, float valueScale) {
        float[] hsv = new float[3];
        android.graphics.Color.colorToHSV(color, hsv);
        hsv[1] = clamp(hsv[1] * saturationScale, 0f, 1f);
        hsv[2] = clamp(hsv[2] * valueScale, 0f, 1f);
        return android.graphics.Color.HSVToColor(hsv);
    }

    private int withAlpha(int color, int alpha) {
        return (color & 0x00FFFFFF) | ((alpha & 0xFF) << 24);
    }

    private float clamp(float value, float min, float max) {
        return Math.max(min, Math.min(max, value));
    }

    private List<AiDetection> deduplicateDetections(List<AiDetection> input) {
        List<AiDetection> sorted = new ArrayList<>(input);
        Collections.sort(sorted, (a, b) -> Float.compare(b.getConfidence(), a.getConfidence()));

        List<AiDetection> unique = new ArrayList<>();
        for (AiDetection candidate : sorted) {
            boolean duplicate = false;
            for (AiDetection kept : unique) {
                if (iou(candidate, kept) >= DEDUP_IOU_THRESHOLD) {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) {
                unique.add(candidate);
            }
        }
        return unique;
    }

    private AiDetection choosePrimaryDetection(List<AiDetection> candidates) {
        AiDetection best = candidates.get(0);
        float bestScore = scoreDetection(best);
        for (int i = 1; i < candidates.size(); i++) {
            AiDetection candidate = candidates.get(i);
            float score = scoreDetection(candidate);
            if (score > bestScore) {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    private float scoreDetection(AiDetection detection) {
        return detection.getConfidence() * 0.7f + area(detection) * 0.3f;
    }

    private float area(AiDetection detection) {
        return Math.max(0f, detection.getX2() - detection.getX1()) * Math.max(0f, detection.getY2() - detection.getY1());
    }

    @Nullable
    private TrackedPerson matchTrack(AiDetection detection) {
        TrackedPerson idMatch = null;
        long sourceTrackId = detection.getTrackId();
        if (sourceTrackId >= 0L) {
            for (TrackedPerson person : trackedPeople.values()) {
                if (!person.matchedInThisFrame && person.sourceTrackId == sourceTrackId) {
                    idMatch = person;
                    break;
                }
            }
        }
        if (idMatch != null) {
            return idMatch;
        }

        TrackedPerson best = null;
        float bestScore = TRACK_MATCH_IOU_THRESHOLD;
        for (TrackedPerson person : trackedPeople.values()) {
            if (person.matchedInThisFrame || person.lastDetection == null) {
                continue;
            }
            float score = iou(person.lastDetection, detection);
            if (score > bestScore) {
                bestScore = score;
                best = person;
            }
        }
        return best;
    }

    private TrackedPerson createTrack(AiDetection detection) {
        long sourceTrackId = detection.getTrackId();
        long stableTrackId = sourceTrackId >= 0L ? sourceTrackId : nextSyntheticTrackId--;
        return new TrackedPerson(stableTrackId, sourceTrackId >= 0L ? sourceTrackId : -1L);
    }

    private void updateTrack(TrackedPerson track, AiDetection incoming) {
        track.matchedInThisFrame = true;
        track.missingFrames = 0;
        if (incoming.getTrackId() >= 0L) {
            track.sourceTrackId = incoming.getTrackId();
        }

        AiDetection previous = track.lastDetection;
        AiDetection blended = previous == null ?
                new AiDetection(
                        incoming.getCls(),
                        incoming.getConfidence(),
                        incoming.getX1(),
                        incoming.getY1(),
                        incoming.getX2(),
                        incoming.getY2(),
                        incoming.getKeypoints(),
                        track.stableTrackId
                ) :
                blendDetections(previous, incoming, track.stableTrackId);

        track.lastDetection = blended;
        track.lastSeenFrame = frameCounter;
    }

    private AiDetection blendDetections(AiDetection previous, AiDetection incoming, long stableTrackId) {
        float x1 = lerp(previous.getX1(), incoming.getX1(), SMOOTH_ALPHA);
        float y1 = lerp(previous.getY1(), incoming.getY1(), SMOOTH_ALPHA);
        float x2 = lerp(previous.getX2(), incoming.getX2(), SMOOTH_ALPHA);
        float y2 = lerp(previous.getY2(), incoming.getY2(), SMOOTH_ALPHA);
        float confidence = lerp(previous.getConfidence(), incoming.getConfidence(), 0.28f);

        List<PoseKeypoint> blendedKeypoints = blendKeypoints(previous.getKeypoints(), incoming.getKeypoints());
        return new AiDetection(incoming.getCls(), confidence, x1, y1, x2, y2, blendedKeypoints, stableTrackId);
    }

    private List<PoseKeypoint> blendKeypoints(List<PoseKeypoint> previous, List<PoseKeypoint> incoming) {
        if (incoming.isEmpty()) {
            return previous;
        }

        SparseArray<PoseKeypoint> previousMap = new SparseArray<>();
        for (PoseKeypoint keypoint : previous) {
            previousMap.put(keypoint.getType(), keypoint);
        }

        List<PoseKeypoint> blended = new ArrayList<>(incoming.size());
        for (PoseKeypoint keypoint : incoming) {
            PoseKeypoint prev = previousMap.get(keypoint.getType());
            if (prev == null) {
                blended.add(keypoint);
                continue;
            }
            blended.add(new PoseKeypoint(
                    keypoint.getType(),
                    lerp(prev.getX(), keypoint.getX(), SMOOTH_ALPHA),
                    lerp(prev.getY(), keypoint.getY(), SMOOTH_ALPHA),
                    lerp(prev.getScore(), keypoint.getScore(), 0.35f)
            ));
        }
        return blended;
    }

    private float iou(AiDetection a, AiDetection b) {
        float interLeft = Math.max(a.getX1(), b.getX1());
        float interTop = Math.max(a.getY1(), b.getY1());
        float interRight = Math.min(a.getX2(), b.getX2());
        float interBottom = Math.min(a.getY2(), b.getY2());

        float interW = Math.max(0f, interRight - interLeft);
        float interH = Math.max(0f, interBottom - interTop);
        float interArea = interW * interH;

        float areaA = Math.max(0f, a.getX2() - a.getX1()) * Math.max(0f, a.getY2() - a.getY1());
        float areaB = Math.max(0f, b.getX2() - b.getX1()) * Math.max(0f, b.getY2() - b.getY1());
        float union = areaA + areaB - interArea;
        if (union <= 0f) {
            return 0f;
        }
        return interArea / union;
    }

    private float lerp(float from, float to, float alpha) {
        return from + (to - from) * alpha;
    }

    private static final class TrackedPerson {
        final long stableTrackId;
        long sourceTrackId;
        int missingFrames;
        long lastSeenFrame;
        boolean matchedInThisFrame;
        @Nullable AiDetection lastDetection;

        TrackedPerson(long stableTrackId, long sourceTrackId) {
            this.stableTrackId = stableTrackId;
            this.sourceTrackId = sourceTrackId;
        }
    }

    private void drawSkeleton(Canvas canvas, AiDetection detection, float scale, float dx, float dy) {
        if (detection.getKeypoints().isEmpty()) {
            return;
        }

        SparseArray<PointF> mappedPoints = new SparseArray<>();
        for (PoseKeypoint keypoint : detection.getKeypoints()) {
            float px = keypoint.getX() * scale + dx;
            float py = keypoint.getY() * scale + dy;
            mappedPoints.put(keypoint.getType(), new PointF(px, py));
            if (keypoint.getScore() >= 0.20f) {
                canvas.drawCircle(px, py, 5f, skeletonPointPaint);
            }
        }

        for (int[] connection : SKELETON_CONNECTIONS) {
            PointF from = mappedPoints.get(connection[0]);
            PointF to = mappedPoints.get(connection[1]);
            if (from == null || to == null) {
                continue;
            }
            canvas.drawLine(from.x, from.y, to.x, to.y, skeletonLinePaint);
        }
    }
}