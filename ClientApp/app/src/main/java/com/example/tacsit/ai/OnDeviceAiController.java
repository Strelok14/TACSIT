package com.example.tacsit.ai;

import android.annotation.SuppressLint;
import android.media.Image;
import android.graphics.PointF;

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

import com.google.common.util.concurrent.ListenableFuture;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.pose.Pose;
import com.google.mlkit.vision.pose.PoseDetection;
import com.google.mlkit.vision.pose.PoseDetector;
import com.google.mlkit.vision.pose.PoseLandmark;
import com.google.mlkit.vision.pose.defaults.PoseDetectorOptions;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class OnDeviceAiController {

    public interface Listener {
        void onDetections(int frameWidth, int frameHeight, List<AiDetection> detections);

        void onError(String message, @Nullable Throwable throwable);
    }

    private final LifecycleOwner lifecycleOwner;
    private final Listener listener;

    private final ExecutorService analyzerExecutor = Executors.newSingleThreadExecutor();
    private final PoseDetector poseDetector;

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

        InputImage inputImage = InputImage.fromMediaImage(mediaImage, imageProxy.getImageInfo().getRotationDegrees());

        poseDetector
                .process(inputImage)
                .addOnSuccessListener(pose -> {
                    List<AiDetection> detections = toDetections(pose, inputImage.getWidth(), inputImage.getHeight());
                    listener.onDetections(inputImage.getWidth(), inputImage.getHeight(), detections);
                })
                .addOnFailureListener(error -> listener.onError("Local person detection failed", error))
                .addOnCompleteListener(task -> imageProxy.close());
    }

    private List<AiDetection> toDetections(@NonNull Pose pose, int frameWidth, int frameHeight) {
        List<AiDetection> detections = new ArrayList<>();
        List<PoseLandmark> landmarks = pose.getAllPoseLandmarks();
        if (landmarks.size() < 5) {
            return detections;
        }

        float minX = Float.MAX_VALUE;
        float minY = Float.MAX_VALUE;
        float maxX = Float.MIN_VALUE;
        float maxY = Float.MIN_VALUE;

        for (PoseLandmark landmark : landmarks) {
            PointF p = landmark.getPosition();
            if (p == null) {
                continue;
            }
            minX = Math.min(minX, p.x);
            minY = Math.min(minY, p.y);
            maxX = Math.max(maxX, p.x);
            maxY = Math.max(maxY, p.y);
        }

        if (minX == Float.MAX_VALUE || minY == Float.MAX_VALUE || maxX <= minX || maxY <= minY) {
            return detections;
        }

        float padX = Math.max(8f, (maxX - minX) * 0.1f);
        float padY = Math.max(8f, (maxY - minY) * 0.15f);

        float x1 = clamp(minX - padX, 0f, frameWidth - 1f);
        float y1 = clamp(minY - padY, 0f, frameHeight - 1f);
        float x2 = clamp(maxX + padX, 0f, frameWidth - 1f);
        float y2 = clamp(maxY + padY, 0f, frameHeight - 1f);

        detections.add(new AiDetection(
                "person",
                0.92f,
                x1,
                y1,
                x2,
                y2
        ));

        return detections;
    }

    private float clamp(float value, float min, float max) {
        return Math.max(min, Math.min(max, value));
    }
}
