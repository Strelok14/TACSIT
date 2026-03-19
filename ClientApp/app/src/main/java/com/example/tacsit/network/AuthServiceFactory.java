package com.example.tacsit.network;

import retrofit2.Retrofit;
import retrofit2.converter.gson.GsonConverterFactory;
import okhttp3.Authenticator;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import okhttp3.Route;
import java.io.IOException;

public final class AuthServiceFactory {

	private static final String DEFAULT_BACKEND_PORT = "5001";

	private AuthServiceFactory() {
	}

	public static AuthApi create(String serverInput) {
		return createRetrofit(serverInput).create(AuthApi.class);
	}

	public static Retrofit createRetrofit(String serverInput) {
		String baseUrl = normalizeBaseUrl(serverInput);
		OkHttpClient client = new OkHttpClient.Builder()
				.addInterceptor(chain -> {
					Request req = chain.request();
					String tok = SessionManager.getAccessToken();
					if (tok == null || tok.isBlank()) {
						return chain.proceed(req);
					}
					return chain.proceed(req.newBuilder()
							.addHeader("Authorization", "Bearer " + tok).build());
				})
				.authenticator(new TokenRefreshAuthenticator(baseUrl))
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
			raw = "https://" + raw;
		}
		int se = raw.indexOf("://");
		int hs = se >= 0 ? se + 3 : 0;
		int ps = raw.indexOf('/', hs);
		String auth = ps >= 0 ? raw.substring(hs, ps) : raw.substring(hs);
		if (!auth.isEmpty() && !auth.contains(":")) {
			raw = raw.substring(0, hs) + auth + ":" + DEFAULT_BACKEND_PORT
					+ (ps >= 0 ? raw.substring(ps) : "");
		}
		return raw.endsWith("/") ? raw : raw + "/";
	}

	/**
	 * OkHttp Authenticator: при 401 пытается обновить access token через refresh.
	 * Если refresh невозможен — очищает сессию (требуется повторная авторизация).
	 */
	private static final class TokenRefreshAuthenticator implements Authenticator {

		private final String baseUrl;

		TokenRefreshAuthenticator(String baseUrl) {
			this.baseUrl = baseUrl;
		}

		@Override
		public Request authenticate(Route route, Response response) throws IOException {
			// Не пытаемся рефрешить сами auth-запросы.
			if (response.request().url().encodedPath().contains("auth/")) {
				SessionManager.clear();
				return null;
			}
			String refresh = SessionManager.getRefreshToken();
			if (refresh == null || refresh.isBlank()) {
				SessionManager.clear();
				return null;
			}
			try {
				OkHttpClient c = new OkHttpClient();
				AuthApi api = new Retrofit.Builder()
						.baseUrl(baseUrl).client(c)
						.addConverterFactory(GsonConverterFactory.create())
						.build().create(AuthApi.class);
				retrofit2.Response<AuthResponse> r =
						api.refresh(new RefreshRequest(refresh)).execute();
				if (r.isSuccessful() && r.body() != null && r.body().isSuccess()) {
					AuthResponse b = r.body();
					SessionManager.setSession(b.getToken(), b.getRefreshToken(), b.getRole());
					return response.request().newBuilder()
							.header("Authorization", "Bearer " + b.getToken())
							.build();
				}
			} catch (Exception ignored) {
			}
			SessionManager.clear();
			return null;
		}
	}
}
