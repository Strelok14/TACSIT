package com.example.tacsit.ai;

import android.content.Context;

import androidx.annotation.Nullable;

import org.webrtc.Camera1Enumerator;
import org.webrtc.Camera2Enumerator;
import org.webrtc.CameraVideoCapturer;
import org.webrtc.DataChannel;
import org.webrtc.DefaultVideoDecoderFactory;
import org.webrtc.DefaultVideoEncoderFactory;
import org.webrtc.EglBase;
import org.webrtc.IceCandidate;
import org.webrtc.MediaConstraints;
import org.webrtc.PeerConnection;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.RendererCommon;
import org.webrtc.SessionDescription;
import org.webrtc.SurfaceTextureHelper;
import org.webrtc.SurfaceViewRenderer;
import org.webrtc.VideoCapturer;
import org.webrtc.VideoSource;
import org.webrtc.VideoTrack;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class WebRtcClient {

    public interface Listener {
        void onLocalOfferReady(SessionDescription offer);

        void onDetectionsMessage(String message);

        void onConnectionStateChanged(String state);

        void onError(String message, @Nullable Throwable throwable);
    }

    private final Context appContext;
    private final EglBase eglBase;
    private final SurfaceViewRenderer localRenderer;
    private final Listener listener;

    private PeerConnectionFactory peerConnectionFactory;
    private PeerConnection peerConnection;
    private VideoCapturer videoCapturer;
    private SurfaceTextureHelper surfaceTextureHelper;
    private VideoSource videoSource;
    private VideoTrack localVideoTrack;
    private DataChannel detectionsChannel;

    private boolean offerDispatched;

    public WebRtcClient(Context appContext, EglBase eglBase, SurfaceViewRenderer localRenderer, Listener listener) {
        this.appContext = appContext.getApplicationContext();
        this.eglBase = eglBase;
        this.localRenderer = localRenderer;
        this.listener = listener;
    }

    public void start() {
        localRenderer.setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT);
        localRenderer.setMirror(false);

        PeerConnectionFactory.initialize(
                PeerConnectionFactory.InitializationOptions.builder(appContext)
                        .createInitializationOptions()
        );

        peerConnectionFactory = PeerConnectionFactory.builder()
                .setVideoEncoderFactory(new DefaultVideoEncoderFactory(eglBase.getEglBaseContext(), true, true))
                .setVideoDecoderFactory(new DefaultVideoDecoderFactory(eglBase.getEglBaseContext()))
                .createPeerConnectionFactory();

        PeerConnection.RTCConfiguration rtcConfiguration = new PeerConnection.RTCConfiguration(new ArrayList<>());
        rtcConfiguration.sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN;
        peerConnection = peerConnectionFactory.createPeerConnection(rtcConfiguration, new PeerConnectionObserver());
        if (peerConnection == null) {
            throw new IllegalStateException("Failed to create PeerConnection");
        }

        detectionsChannel = peerConnection.createDataChannel("detections", new DataChannel.Init());
        if (detectionsChannel != null) {
            registerDataChannel(detectionsChannel);
        } else {
            listener.onError("Failed to create detections data channel", null);
        }

        videoCapturer = createVideoCapturer();
        if (videoCapturer == null) {
            throw new IllegalStateException("No compatible camera capturer available");
        }

        surfaceTextureHelper = SurfaceTextureHelper.create("TacsitCaptureThread", eglBase.getEglBaseContext());
        videoSource = peerConnectionFactory.createVideoSource(videoCapturer.isScreencast());
        videoCapturer.initialize(surfaceTextureHelper, appContext, videoSource.getCapturerObserver());
        videoCapturer.startCapture(1280, 720, 30);

        localVideoTrack = peerConnectionFactory.createVideoTrack("camera-track", videoSource);
        localVideoTrack.addSink(localRenderer);
        peerConnection.addTrack(localVideoTrack, Collections.singletonList("camera-stream"));
    }

    public void createOffer() {
        if (peerConnection == null) {
            listener.onError("PeerConnection is not initialized", null);
            return;
        }

        offerDispatched = false;
        MediaConstraints constraints = new MediaConstraints();
        constraints.mandatory.add(new MediaConstraints.KeyValuePair("OfferToReceiveAudio", "false"));
        constraints.mandatory.add(new MediaConstraints.KeyValuePair("OfferToReceiveVideo", "false"));

        peerConnection.createOffer(new SimpleSdpObserver() {
            @Override
            public void onCreateSuccess(SessionDescription sessionDescription) {
                peerConnection.setLocalDescription(new SimpleSdpObserver() {
                    @Override
                    public void onSetSuccess() {
                        maybeDispatchLocalOffer();
                    }

                    @Override
                    public void onSetFailure(String error) {
                        listener.onError("Failed to set local description: " + error, null);
                    }
                }, sessionDescription);
            }

            @Override
            public void onCreateFailure(String error) {
                listener.onError("Failed to create WebRTC offer: " + error, null);
            }
        }, constraints);
    }

    public void applyAnswer(String sdp) {
        if (peerConnection == null) {
            listener.onError("PeerConnection is not initialized", null);
            return;
        }
        SessionDescription answer = new SessionDescription(SessionDescription.Type.ANSWER, sdp);
        peerConnection.setRemoteDescription(new SimpleSdpObserver() {
            @Override
            public void onSetFailure(String error) {
                listener.onError("Failed to apply remote answer: " + error, null);
            }
        }, answer);
    }

    public void release() {
        if (detectionsChannel != null) {
            detectionsChannel.close();
            detectionsChannel = null;
        }
        if (peerConnection != null) {
            peerConnection.close();
            peerConnection = null;
        }
        if (localVideoTrack != null) {
            localVideoTrack.dispose();
            localVideoTrack = null;
        }
        if (videoCapturer != null) {
            try {
                videoCapturer.stopCapture();
            } catch (InterruptedException interruptedException) {
                Thread.currentThread().interrupt();
            }
            videoCapturer.dispose();
            videoCapturer = null;
        }
        if (videoSource != null) {
            videoSource.dispose();
            videoSource = null;
        }
        if (surfaceTextureHelper != null) {
            surfaceTextureHelper.dispose();
            surfaceTextureHelper = null;
        }
        if (peerConnectionFactory != null) {
            peerConnectionFactory.dispose();
            peerConnectionFactory = null;
        }
    }

    private void maybeDispatchLocalOffer() {
        if (offerDispatched || peerConnection == null || peerConnection.getLocalDescription() == null) {
            return;
        }
        if (peerConnection.iceGatheringState() != PeerConnection.IceGatheringState.COMPLETE) {
            return;
        }
        offerDispatched = true;
        listener.onLocalOfferReady(peerConnection.getLocalDescription());
    }

    private void registerDataChannel(DataChannel dataChannel) {
        if (dataChannel == null) {
            listener.onError("DataChannel is null", null);
            return;
        }

        dataChannel.registerObserver(new DataChannel.Observer() {
            @Override
            public void onBufferedAmountChange(long previousAmount) {
            }

            @Override
            public void onStateChange() {
                listener.onConnectionStateChanged("datachannel=" + dataChannel.state());
            }

            @Override
            public void onMessage(DataChannel.Buffer buffer) {
                ByteBuffer data = buffer.data;
                byte[] bytes = new byte[data.remaining()];
                data.get(bytes);
                listener.onDetectionsMessage(new String(bytes, StandardCharsets.UTF_8));
            }
        });
    }

    @Nullable
    private VideoCapturer createVideoCapturer() {
        try {
            Camera2Enumerator enumerator = new Camera2Enumerator(appContext);
            String[] deviceNames = enumerator.getDeviceNames();

            for (String deviceName : deviceNames) {
                if (!enumerator.isFrontFacing(deviceName)) {
                    CameraVideoCapturer capturer = enumerator.createCapturer(deviceName, null);
                    if (capturer != null) {
                        return capturer;
                    }
                }
            }

            for (String deviceName : deviceNames) {
                CameraVideoCapturer capturer = enumerator.createCapturer(deviceName, null);
                if (capturer != null) {
                    return capturer;
                }
            }
        } catch (RuntimeException exception) {
            listener.onError("Camera2 capturer failed, trying Camera1 fallback", exception);
        }

        // Fallback for devices/drivers where Camera2 capturer cannot be created.
        Camera1Enumerator fallbackEnumerator = new Camera1Enumerator(false);
        String[] fallbackDevices = fallbackEnumerator.getDeviceNames();

        for (String deviceName : fallbackDevices) {
            if (!fallbackEnumerator.isFrontFacing(deviceName)) {
                CameraVideoCapturer capturer = fallbackEnumerator.createCapturer(deviceName, null);
                if (capturer != null) {
                    return capturer;
                }
            }
        }

        for (String deviceName : fallbackDevices) {
            CameraVideoCapturer capturer = fallbackEnumerator.createCapturer(deviceName, null);
            if (capturer != null) {
                return capturer;
            }
        }

        return null;
    }

    private final class PeerConnectionObserver implements PeerConnection.Observer {

        @Override
        public void onSignalingChange(PeerConnection.SignalingState signalingState) {
            listener.onConnectionStateChanged("signaling=" + signalingState);
        }

        @Override
        public void onIceConnectionChange(PeerConnection.IceConnectionState iceConnectionState) {
            listener.onConnectionStateChanged("ice=" + iceConnectionState);
        }

        @Override
        public void onIceConnectionReceivingChange(boolean receiving) {
        }

        @Override
        public void onIceGatheringChange(PeerConnection.IceGatheringState iceGatheringState) {
            listener.onConnectionStateChanged("gathering=" + iceGatheringState);
            maybeDispatchLocalOffer();
        }

        @Override
        public void onIceCandidate(IceCandidate iceCandidate) {
        }

        @Override
        public void onIceCandidatesRemoved(IceCandidate[] iceCandidates) {
        }

        @Override
        public void onAddStream(org.webrtc.MediaStream mediaStream) {
        }

        @Override
        public void onRemoveStream(org.webrtc.MediaStream mediaStream) {
        }

        @Override
        public void onDataChannel(DataChannel dataChannel) {
            registerDataChannel(dataChannel);
        }

        @Override
        public void onRenegotiationNeeded() {
        }

        @Override
        public void onAddTrack(org.webrtc.RtpReceiver rtpReceiver, org.webrtc.MediaStream[] mediaStreams) {
        }

        @Override
        public void onConnectionChange(PeerConnection.PeerConnectionState newState) {
            listener.onConnectionStateChanged("peer=" + newState);
        }

        @Override
        public void onStandardizedIceConnectionChange(PeerConnection.IceConnectionState newState) {
        }

        @Override
        public void onTrack(org.webrtc.RtpTransceiver transceiver) {
        }
    }
}