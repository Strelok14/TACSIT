package com.example.tacsit.network;

import retrofit2.Retrofit;
import retrofit2.converter.gson.GsonConverterFactory;
import okhttp3.OkHttpClient;
import okhttp3.Request;

public final class AuthServiceFactory {

    private static final String DEFAULT_BACKEND_PORT = "5001";

    private AuthServiceFactory() {
    }

    public static AuthApi create(String serverInput) {
        Retrofit retrofit = createRetrofit(serverInput);
        return retrofit.create(AuthApi.class);
    }

    public static Retrofit createRetrofit(String serverInput) {
        String baseUrl = normalizeBaseUrl(serverInput);

        OkHttpClient client = new OkHttpClient.Builder()
                .addInterceptor(chain -> {
                    Request request = chain.request();
                    String token = SessionManager.getAccessToken();

                    if (token == null || token.isBlank()) {
                        return chain.proceed(request);
                    }

                    Request secured = request.newBuilder()
                            .addHeader("Authorization", "Bearer " + token)
                            .build();

                    return chain.proceed(secured);
                })
                .build();

        return new Retrofit.Builder()
                .baseUrl(baseUrl)
                .client(client)
                .addConverterFactory(GsonConverterFactory.create())
                .build();
    }

    private static String normalizeBaseUrl(String value) {
        String raw = value == null ? "" : value.trim();
        if (!raw.startsWith("http://") && !raw.startsWith("https://")) {
            // По умолчанию используем защищённый протокол.
            raw = "https://" + raw;
        }

        int schemeEnd = raw.indexOf("://");
        int hostStart = schemeEnd >= 0 ? schemeEnd + 3 : 0;
        int pathStart = raw.indexOf('/', hostStart);
        String authority = pathStart >= 0 ? raw.substring(hostStart, pathStart) : raw.substring(hostStart);

        if (!authority.isEmpty() && !authority.contains(":")) {
            raw = raw.substring(0, hostStart) + authority + ":" + DEFAULT_BACKEND_PORT + (pathStart >= 0 ? raw.substring(pathStart) : "");
        }

        if (!raw.endsWith("/")) {
            raw = raw + "/";
        }
        return raw;
    }
}
