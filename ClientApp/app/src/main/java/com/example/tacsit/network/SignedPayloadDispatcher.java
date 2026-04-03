package com.example.tacsit.network;

import android.util.Base64;

import com.google.gson.Gson;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Objects;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

import okhttp3.MediaType;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;

public final class SignedPayloadDispatcher {

    private static final MediaType JSON = MediaType.get("application/json; charset=utf-8");
    private static final Gson GSON = new Gson();

    private SignedPayloadDispatcher() {
    }

    public static boolean postSignedJson(String serverUrl, String path, Object payload) {
        try {
            Integer userId = SessionManager.getUserId();
            String hmacKey = SessionManager.getHmacKey();
            String accessToken = SessionManager.getAccessToken();
            if (userId == null || hmacKey == null || accessToken == null) {
                return false;
            }

            String normalizedBaseUrl = AuthServiceFactory.createRetrofit(serverUrl).baseUrl().toString();
            String body = GSON.toJson(payload);
            long sequence = SessionManager.nextPacketSequence();
            long timestamp = System.currentTimeMillis();
            String canonical = userId + "|" + sequence + "|" + timestamp + "|" + body;
            String signature = sign(canonical, hmacKey);

            OkHttpClient client = AuthServiceFactory.createAuthenticatedHttpClient(serverUrl);
            Request request = new Request.Builder()
                    .url(normalizedBaseUrl + path)
                    .post(RequestBody.create(body, JSON))
                    .header("Authorization", "Bearer " + accessToken)
                    .header("X-User-Id", String.valueOf(userId))
                    .header("X-Sequence", String.valueOf(sequence))
                    .header("X-Timestamp", String.valueOf(timestamp))
                    .header("X-Signature", signature)
                    .build();

            try (Response response = client.newCall(request).execute()) {
                return response.isSuccessful();
            }
        } catch (Exception exception) {
            return false;
        }
    }

    private static String sign(String canonical, String base64Key) throws GeneralSecurityException {
        byte[] keyBytes = Base64.decode(Objects.requireNonNull(base64Key), Base64.DEFAULT);
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(keyBytes, "HmacSHA256"));
        byte[] signature = mac.doFinal(canonical.getBytes(StandardCharsets.UTF_8));
        return Base64.encodeToString(signature, Base64.NO_WRAP);
    }
}