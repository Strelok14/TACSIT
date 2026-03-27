package com.example.tacsit.ai;

import android.annotation.SuppressLint;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.ImageFormat;
import android.graphics.Matrix;
import android.media.Image;
import android.graphics.PointF;
import android.graphics.Rect;
import android.graphics.YuvImage;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageProxy;
import androidx.camera.core.Preview;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.camera.view.PreviewView;
import androidx.core.content.ContextCompat;
import androidx.lifecycle.LifecycleOwner;

import com.google.android.gms.tasks.Task;
import com.google.android.gms.tasks.Tasks;
import com.google.common.util.concurrent.ListenableFuture;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.pose.Pose;
import com.google.mlkit.vision.pose.PoseDetection;
import com.google.mlkit.vision.pose.PoseDetector;
import com.google.mlkit.vision.pose.PoseLandmark;
import com.google.mlkit.vision.pose.defaults.PoseDetectorOptions;
import com.google.mlkit.vision.objects.DetectedObject;
import com.google.mlkit.vision.objects.ObjectDetection;
import com.google.mlkit.vision.objects.ObjectDetector;
import com.google.mlkit.vision.objects.defaults.ObjectDetectorOptions;

import java.io.ByteArrayOutputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;
import java.nio.ByteBuffer;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class OnDeviceAiController {

    private static final boolean SINGLE_PERSON_MODE = true;
    private static final float OBJECT_NMS_IOU_THRESHOLD = 0.40f;
    private static final float MIN_OBJECT_CONFIDENCE = 0.48f;
    private static final int MIN_POSE_KEYPOINTS = 5;
    private static final float MIN_POSE_MEAN_SCORE = 0.24f;

    public interface Listener {
        void onDetections(int frameWidth, int frameHeight, List<AiDetection> detections);

        void onError(String message, @Nullable Throwable throwable);
    }

    private final LifecycleOwner lifecycleOwner;
    private final Listener listener;

    private final ExecutorService analyzerExecutor = Executors.newSingleThreadExecutor();
    private final PoseDetector poseDetector;
    private final ObjectDetector objectDetector;

    private ProcessCameraProvider cameraProvider;
    private long lastAnalyzedAtMs;

    public OnDeviceAiController(@NonNull LifecycleOwner lifecycleOwner, @NonNull Listener listener) {
        this.lifecycleOwner = lifecycleOwner;
        this.listener = listener;

        PoseDetectorOptions options =
            new PoseDetectorOptions.Builder()
                .setDetectorMode(PoseDetectorOptions.STREAM_MODE)
                .build();
        poseDetector = PoseDetection.getClient(options);

        ObjectDetectorOptions objectOptions =
            new ObjectDetectorOptions.Builder()
                .setDetectorMode(ObjectDetectorOptions.STREAM_MODE)
                .enableMultipleObjects()
                .enableClassification()
                .build();
        objectDetector = ObjectDetection.getClient(objectOptions);
    }

    public void start(@NonNull PreviewView previewView) {
        ListenableFuture<ProcessCameraProvider> providerFuture = ProcessCameraProvider.getInstance(previewView.getContext());
        providerFuture.addListener(() -> {
            try {
                cameraProvider = providerFuture.get();
                bindUseCases(previewView);
            } catch (Exception exception) {
                listener.onError("Failed to start local AI camera", exception);
            }
        }, ContextCompat.getMainExecutor(previewView.getContext()));
    }

    public void stop() {
        if (cameraProvider != null) {
            cameraProvider.unbindAll();
        }
        lastAnalyzedAtMs = 0L;
    }

    public void release() {
        stop();
        poseDetector.close();
        objectDetector.close();
        analyzerExecutor.shutdown();
    }

    @SuppressLint("UnsafeOptInUsageError")
    private void bindUseCases(@NonNull PreviewView previewView) {
        if (cameraProvider == null) {
            return;
        }

        cameraProvider.unbindAll();

        Preview preview = new Preview.Builder().build();
        preview.setSurfaceProvider(previewView.getSurfaceProvider());

        ImageAnalysis imageAnalysis =
                new ImageAnalysis.Builder()
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                        .build();

        imageAnalysis.setAnalyzer(analyzerExecutor, imageProxy -> analyzeFrame(imageProxy));

        CameraSelector cameraSelector = CameraSelector.DEFAULT_BACK_CAMERA;

        cameraProvider.bindToLifecycle(lifecycleOwner, cameraSelector, preview, imageAnalysis);
    }

    private void analyzeFrame(@NonNull ImageProxy imageProxy) {
        long now = System.currentTimeMillis();
        if (now - lastAnalyzedAtMs < 200L) {
            imageProxy.close();
            return;
        }
        lastAnalyzedAtMs = now;

        Image mediaImage = imageProxy.getImage();
        if (mediaImage == null) {
            imageProxy.close();
            return;
        }

        int rotationDegrees = imageProxy.getImageInfo().getRotationDegrees();
        Bitmap frameBitmap = toUprightBitmap(imageProxy, rotationDegrees);
        imageProxy.close();
        if (frameBitmap == null) {
            return;
        }

        int frameWidth = frameBitmap.getWidth();
        int frameHeight = frameBitmap.getHeight();
        InputImage inputImage = InputImage.fromBitmap(frameBitmap, 0);

        objectDetector
                .process(inputImage)
                .addOnSuccessListener(objects -> processObjects(frameBitmap, frameWidth, frameHeight, objects))
                .addOnFailureListener(error -> listener.onError("Local person detection failed", error))
                .addOnCompleteListener(task -> frameBitmap.recycle());
    }

    private void processObjects(
            @NonNull Bitmap frameBitmap,
            int frameWidth,
            int frameHeight,
            @NonNull List<DetectedObject> objects
    ) {
        List<DetectedObject> personObjects = filterPersonObjects(objects, frameWidth, frameHeight);
        personObjects = selectObjectCandidates(personObjects, frameWidth, frameHeight);
        if (personObjects.isEmpty()) {
            listener.onDetections(frameWidth, frameHeight, Collections.emptyList());
            return;
        }

        List<Task<AiDetection>> poseTasks = new ArrayList<>();
        for (int index = 0; index < personObjects.size(); index++) {
            DetectedObject detectedObject = personObjects.get(index);
            Rect box = sanitizeRect(detectedObject.getBoundingBox(), frameWidth, frameHeight);
            if (box.width() < 16 || box.height() < 16) {
                continue;
            }

            Bitmap personBitmap = Bitmap.createBitmap(frameBitmap, box.left, box.top, box.width(), box.height());
            InputImage personImage = InputImage.fromBitmap(personBitmap, 0);

            int fallbackTrack = (int) (box.centerX() + box.centerY() * 31L);
            long trackId = detectedObject.getTrackingId() == null
                    ? fallbackTrack
                    : detectedObject.getTrackingId().longValue();
            float confidence = extractConfidence(detectedObject);

            Task<AiDetection> poseTask = poseDetector
                    .process(personImage)
                    .continueWith(task -> {
                        personBitmap.recycle();
                        if (!task.isSuccessful() || task.getResult() == null) {
                            return bboxOnlyDetection(box, frameWidth, frameHeight, confidence, trackId);
                        }
                        AiDetection poseDetection = toDetection(task.getResult(), box.left, box.top, frameWidth, frameHeight, confidence, trackId);
                        if (poseDetection != null) {
                            return poseDetection;
                        }
                        return bboxOnlyDetection(box, frameWidth, frameHeight, confidence * 0.92f, trackId);
                    });
            poseTasks.add(poseTask);
        }

        if (poseTasks.isEmpty()) {
            listener.onDetections(frameWidth, frameHeight, Collections.emptyList());
            return;
        }

        Tasks.whenAllSuccess(poseTasks)
                .addOnSuccessListener(results -> {
                    List<AiDetection> detections = new ArrayList<>();
                    for (Object result : results) {
                        if (result instanceof AiDetection) {
                            detections.add((AiDetection) result);
                        }
                    }
                    detections = selectFinalDetections(detections, frameWidth, frameHeight);
                    listener.onDetections(frameWidth, frameHeight, detections);
                })
                .addOnFailureListener(error -> listener.onError("Local multi-person pose failed", error));
    }

    private List<DetectedObject> selectObjectCandidates(
            @NonNull List<DetectedObject> personObjects,
            int frameWidth,
            int frameHeight
    ) {
        if (personObjects.isEmpty()) {
            return personObjects;
        }

        class Candidate {
            final DetectedObject object;
            final Rect box;
            final float confidence;
            final float score;

            Candidate(DetectedObject object, Rect box, float confidence, float score) {
                this.object = object;
                this.box = box;
                this.confidence = confidence;
                this.score = score;
            }
        }

        float frameArea = Math.max(1f, frameWidth * (float) frameHeight);
        List<Candidate> candidates = new ArrayList<>();
        for (DetectedObject object : personObjects) {
            Rect box = sanitizeRect(object.getBoundingBox(), frameWidth, frameHeight);
            float conf = extractConfidence(object);
            if (conf < MIN_OBJECT_CONFIDENCE) {
                continue;
            }

            float areaNorm = (box.width() * box.height()) / frameArea;
            float cx = box.centerX() / (float) frameWidth;
            float cy = box.centerY() / (float) frameHeight;
            float centerDistance = Math.abs(cx - 0.5f) + Math.abs(cy - 0.5f);
            float centerScore = Math.max(0f, 1f - centerDistance);
            float score = conf * 0.70f + areaNorm * 0.20f + centerScore * 0.10f;
            candidates.add(new Candidate(object, box, conf, score));
        }

        if (candidates.isEmpty()) {
            return Collections.emptyList();
        }

        candidates.sort((a, b) -> Float.compare(b.score, a.score));

        List<Candidate> kept = new ArrayList<>();
        for (Candidate candidate : candidates) {
            boolean overlaps = false;
            for (Candidate accepted : kept) {
                if (rectIou(candidate.box, accepted.box) >= OBJECT_NMS_IOU_THRESHOLD) {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps) {
                kept.add(candidate);
            }
        }

        if (kept.isEmpty()) {
            return Collections.emptyList();
        }

        List<DetectedObject> selected = new ArrayList<>();
        if (SINGLE_PERSON_MODE) {
            selected.add(kept.get(0).object);
            return selected;
        }

        int limit = Math.min(2, kept.size());
        for (int i = 0; i < limit; i++) {
            selected.add(kept.get(i).object);
        }
        return selected;
    }

    private List<AiDetection> selectFinalDetections(
            @NonNull List<AiDetection> detections,
            int frameWidth,
            int frameHeight
    ) {
        if (detections.isEmpty()) {
            return detections;
        }

        if (!SINGLE_PERSON_MODE) {
            return detections;
        }

        float frameArea = Math.max(1f, frameWidth * (float) frameHeight);
        AiDetection best = detections.get(0);
        float bestScore = detectionScore(best, frameArea, frameWidth, frameHeight);
        for (int i = 1; i < detections.size(); i++) {
            AiDetection candidate = detections.get(i);
            float score = detectionScore(candidate, frameArea, frameWidth, frameHeight);
            if (score > bestScore) {
                bestScore = score;
                best = candidate;
            }
        }

        List<AiDetection> onlyOne = new ArrayList<>(1);
        onlyOne.add(best);
        return onlyOne;
    }

    private float detectionScore(AiDetection detection, float frameArea, int frameWidth, int frameHeight) {
        float areaNorm = (Math.max(0f, detection.getX2() - detection.getX1())
                * Math.max(0f, detection.getY2() - detection.getY1())) / frameArea;
        float cx = ((detection.getX1() + detection.getX2()) * 0.5f) / Math.max(1f, frameWidth);
        float cy = ((detection.getY1() + detection.getY2()) * 0.5f) / Math.max(1f, frameHeight);
        float centerDistance = Math.abs(cx - 0.5f) + Math.abs(cy - 0.5f);
        float centerScore = Math.max(0f, 1f - centerDistance);
        return detection.getConfidence() * 0.70f + areaNorm * 0.20f + centerScore * 0.10f;
    }

    private float rectIou(@NonNull Rect a, @NonNull Rect b) {
        int interLeft = Math.max(a.left, b.left);
        int interTop = Math.max(a.top, b.top);
        int interRight = Math.min(a.right, b.right);
        int interBottom = Math.min(a.bottom, b.bottom);

        float interW = Math.max(0f, interRight - interLeft);
        float interH = Math.max(0f, interBottom - interTop);
        float interArea = interW * interH;

        float areaA = Math.max(0f, a.width()) * Math.max(0f, a.height());
        float areaB = Math.max(0f, b.width()) * Math.max(0f, b.height());
        float union = areaA + areaB - interArea;
        if (union <= 0f) {
            return 0f;
        }
        return interArea / union;
    }

    private List<DetectedObject> filterPersonObjects(
            @NonNull List<DetectedObject> objects,
            int frameWidth,
            int frameHeight
    ) {
        List<DetectedObject> persons = new ArrayList<>();
        float minW = Math.max(20f, frameWidth * 0.03f);
        float minH = Math.max(28f, frameHeight * 0.05f);

        for (DetectedObject object : objects) {
            Rect box = sanitizeRect(object.getBoundingBox(), frameWidth, frameHeight);
            if (box.width() < minW || box.height() < minH) {
                continue;
            }
            if (isLikelyPerson(object)) {
                persons.add(object);
            }
        }

        if (!persons.isEmpty()) {
            return persons;
        }

        for (DetectedObject object : objects) {
            Rect box = sanitizeRect(object.getBoundingBox(), frameWidth, frameHeight);
            if (box.width() >= minW && box.height() >= minH) {
                persons.add(object);
            }
        }
        return persons;
    }

    private boolean isLikelyPerson(@NonNull DetectedObject object) {
        List<DetectedObject.Label> labels = object.getLabels();
        if (labels.isEmpty()) {
            return true;
        }
        for (DetectedObject.Label label : labels) {
            String text = label.getText();
            if (text != null && text.toLowerCase(Locale.US).contains("person")) {
                return true;
            }
        }
        return false;
    }

    private float extractConfidence(@NonNull DetectedObject object) {
        float confidence = 0.70f;
        for (DetectedObject.Label label : object.getLabels()) {
            confidence = Math.max(confidence, label.getConfidence());
        }
        return clamp(confidence, 0.35f, 0.99f);
    }

    private Rect sanitizeRect(@NonNull Rect source, int frameWidth, int frameHeight) {
        int left = Math.max(0, Math.min(source.left, frameWidth - 1));
        int top = Math.max(0, Math.min(source.top, frameHeight - 1));
        int right = Math.max(left + 1, Math.min(source.right, frameWidth));
        int bottom = Math.max(top + 1, Math.min(source.bottom, frameHeight));
        return new Rect(left, top, right, bottom);
    }

    private AiDetection toDetection(
            @NonNull Pose pose,
            int offsetX,
            int offsetY,
            int frameWidth,
            int frameHeight,
            float confidence,
            long trackId
    ) {
        List<PoseLandmark> landmarks = pose.getAllPoseLandmarks();
        if (landmarks.size() < MIN_POSE_KEYPOINTS) {
            return null;
        }

        float minX = Float.MAX_VALUE;
        float minY = Float.MAX_VALUE;
        float maxX = Float.MIN_VALUE;
        float maxY = Float.MIN_VALUE;
        List<PoseKeypoint> keypoints = new ArrayList<>();
        float scoreSum = 0f;
        int scoredCount = 0;

        for (PoseLandmark landmark : landmarks) {
            PointF p = landmark.getPosition();
            if (p == null) {
                continue;
            }
            float absoluteX = p.x + offsetX;
            float absoluteY = p.y + offsetY;
            keypoints.add(new PoseKeypoint(
                    landmark.getLandmarkType(),
                    absoluteX,
                    absoluteY,
                    landmark.getInFrameLikelihood()
            ));
            scoreSum += landmark.getInFrameLikelihood();
            scoredCount++;
            minX = Math.min(minX, absoluteX);
            minY = Math.min(minY, absoluteY);
            maxX = Math.max(maxX, absoluteX);
            maxY = Math.max(maxY, absoluteY);
        }

        if (minX == Float.MAX_VALUE || minY == Float.MAX_VALUE || maxX <= minX || maxY <= minY) {
            return null;
        }

        if (keypoints.size() < MIN_POSE_KEYPOINTS) {
            return null;
        }

        float meanScore = scoredCount > 0 ? (scoreSum / scoredCount) : 0f;
        if (meanScore < MIN_POSE_MEAN_SCORE) {
            return null;
        }

        float padX = Math.max(8f, (maxX - minX) * 0.1f);
        float padY = Math.max(8f, (maxY - minY) * 0.15f);

        float x1 = clamp(minX - padX, 0f, frameWidth - 1f);
        float y1 = clamp(minY - padY, 0f, frameHeight - 1f);
        float x2 = clamp(maxX + padX, 0f, frameWidth - 1f);
        float y2 = clamp(maxY + padY, 0f, frameHeight - 1f);

        return new AiDetection(
                "person",
                confidence,
                x1,
                y1,
                x2,
                y2,
                keypoints,
                trackId
        );
    }

            private AiDetection bboxOnlyDetection(
                @NonNull Rect box,
                int frameWidth,
                int frameHeight,
                float confidence,
                long trackId
            ) {
            float padX = Math.max(4f, box.width() * 0.05f);
            float padY = Math.max(4f, box.height() * 0.07f);

            float x1 = clamp(box.left - padX, 0f, frameWidth - 1f);
            float y1 = clamp(box.top - padY, 0f, frameHeight - 1f);
            float x2 = clamp(box.right + padX, 0f, frameWidth - 1f);
            float y2 = clamp(box.bottom + padY, 0f, frameHeight - 1f);

            return new AiDetection(
                "person",
                clamp(confidence, 0.35f, 0.95f),
                x1,
                y1,
                x2,
                y2,
                Collections.emptyList(),
                trackId
            );
            }

    @Nullable
    private Bitmap toUprightBitmap(@NonNull ImageProxy imageProxy, int rotationDegrees) {
        byte[] nv21 = yuv420ToNv21(imageProxy);
        if (nv21 == null) {
            return null;
        }

        YuvImage yuvImage = new YuvImage(
                nv21,
                ImageFormat.NV21,
                imageProxy.getWidth(),
                imageProxy.getHeight(),
                null
        );

        ByteArrayOutputStream outputStream = new ByteArrayOutputStream();
        boolean encoded = yuvImage.compressToJpeg(
                new Rect(0, 0, imageProxy.getWidth(), imageProxy.getHeight()),
                88,
                outputStream
        );
        if (!encoded) {
            return null;
        }

        byte[] jpegBytes = outputStream.toByteArray();
        Bitmap bitmap = BitmapFactory.decodeByteArray(jpegBytes, 0, jpegBytes.length);
        if (bitmap == null) {
            return null;
        }
        if (rotationDegrees == 0) {
            return bitmap;
        }

        Matrix matrix = new Matrix();
        matrix.postRotate(rotationDegrees);
        Bitmap rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), matrix, true);
        if (rotated != bitmap) {
            bitmap.recycle();
        }
        return rotated;
    }

    @Nullable
    private byte[] yuv420ToNv21(@NonNull ImageProxy imageProxy) {
        ImageProxy.PlaneProxy[] planes = imageProxy.getPlanes();
        if (planes.length < 3) {
            return null;
        }

        ByteBuffer yBuffer = planes[0].getBuffer();
        ByteBuffer uBuffer = planes[1].getBuffer();
        ByteBuffer vBuffer = planes[2].getBuffer();

        int ySize = yBuffer.remaining();
        int uSize = uBuffer.remaining();
        int vSize = vBuffer.remaining();

        byte[] nv21 = new byte[ySize + uSize + vSize];
        yBuffer.get(nv21, 0, ySize);
        vBuffer.get(nv21, ySize, vSize);
        uBuffer.get(nv21, ySize + vSize, uSize);
        return nv21;
    }

    private float clamp(float value, float min, float max) {
        return Math.max(min, Math.min(max, value));
    }
}
