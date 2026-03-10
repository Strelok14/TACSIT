package com.example.tacsit.network;

import retrofit2.Retrofit;
import retrofit2.converter.gson.GsonConverterFactory;

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
        return new Retrofit.Builder()
                .baseUrl(baseUrl)
                .addConverterFactory(GsonConverterFactory.create())
                .build();
    }

    private static String normalizeBaseUrl(String value) {
        String raw = value == null ? "" : value.trim();
        if (!raw.startsWith("http://") && !raw.startsWith("https://")) {
            raw = "http://" + raw;
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
