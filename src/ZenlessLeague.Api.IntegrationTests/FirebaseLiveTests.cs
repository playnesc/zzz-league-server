using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Infrastructure.Firebase;

namespace ZenlessLeague.Api.IntegrationTests;

// Hits the REAL zzz-league Firebase project (Storage bucket + Auth) with the real clients — the
// fakes in the unit suite can't tell whether the project's plan/billing/config still lets them work.
// Opt-in only: set FIREBASE_LIVE_TESTS=1. Credentials come from FIREBASE_CREDENTIALS_PATH, else the
// gitignored Migrator copy of the service-account file. Everything created is cleaned up.
public class FirebaseLiveTests
{
	private const string ProjectId = "zzz-league";
	private const string StorageBucket = "zzz-league.firebasestorage.app";
	// Public web API key, same as the frontend's firebase.ts.
	private const string WebApiKey = "AIzaSyAlcnUiLJ1cq7ekCQFi_NOPAQ6UiG92ZqM";

	// Smallest valid PNG: 1x1 transparent pixel.
	private static readonly byte[] Png = Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

	private static readonly HttpClient Http = new();

	[FirebaseLiveFact]
	public async Task Storage_UploadIsPubliclyReadable_AndDeleteRemovesIt()
	{
		var credential = CreateStorageCredential();
		var client = new FirebaseStorageClient(Http, new Lazy<GoogleCredential>(() => credential),
			Options.Create(new FirebaseStorageOptions { StorageBucket = StorageBucket }));
		var prefix = $"smoke-tests/{Guid.NewGuid():N}";

		string url;
		try
		{
			url = await client.UploadImageAsync($"data:image/png;base64,{Convert.ToBase64String(Png)}", prefix);
		}
		catch (Exception ex)
		{
			throw new Xunit.Sdk.XunitException($"Upload to gs://{StorageBucket} failed: {ex.Message}");
		}

		try
		{
			Assert.Equal($"https://storage.googleapis.com/{StorageBucket}/{prefix}.png", url);

			// Anonymous read, exactly how the site serves avatars/logos.
			using var publicResponse = await Http.GetAsync(url);
			Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
			Assert.Equal(Png, await publicResponse.Content.ReadAsByteArrayAsync());
		}
		finally
		{
			await client.DeleteImageAsync(prefix);
		}

		// Checked via the authenticated metadata API — public URLs may be edge-cached after deletion.
		Assert.Equal(HttpStatusCode.NotFound, await GetObjectMetadataStatusAsync(credential, $"{prefix}.png"));
	}

	[FirebaseLiveFact]
	public async Task Auth_CustomToken_ExchangesForIdTokenTheApiAccepts()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["Firebase:CredentialsPath"] = CredentialsPath() })
			.Build();
		var uid = $"smoke-test-{Guid.NewGuid():N}";
		var customToken = new FirebaseCustomTokenSigner(configuration).CreateCustomToken(uid);

		// What the frontend's signInWithCustomToken does under the hood.
		using var signInResponse = await Http.PostAsJsonAsync(
			$"https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key={WebApiKey}",
			new { token = customToken, returnSecureToken = true });
		var signInBody = await signInResponse.Content.ReadAsStringAsync();
		Assert.True(signInResponse.IsSuccessStatusCode, $"signInWithCustomToken failed: {signInBody}");
		var idToken = JsonDocument.Parse(signInBody).RootElement.GetProperty("idToken").GetString()!;

		try
		{
			// Same validation AuthFeature configures for the Firebase JwtBearer scheme.
			var authority = $"https://securetoken.google.com/{ProjectId}";
			var oidc = await new ConfigurationManager<OpenIdConnectConfiguration>(
				$"{authority}/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever()).GetConfigurationAsync();
			var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
			{
				ValidIssuer = authority,
				ValidAudience = ProjectId,
				IssuerSigningKeys = oidc.SigningKeys,
				ValidateIssuer = true,
				ValidateAudience = true,
				ValidateLifetime = true,
			});

			Assert.True(result.IsValid, $"ID token rejected: {result.Exception?.Message}");
			Assert.Equal(uid, result.Claims["sub"]);
		}
		finally
		{
			await DeleteSmokeTestAuthUsersAsync();
		}
	}

	// Removes the auto-provisioned Auth user(s) — including any left behind by an earlier crashed run.
	// Admin API with the service account: self-delete via the web key is disabled on this project
	// (ADMIN_ONLY_OPERATION).
	private static async Task DeleteSmokeTestAuthUsersAsync()
	{
#pragma warning disable CS0618
		var credential = GoogleCredential.FromFile(CredentialsPath()).CreateScoped("https://www.googleapis.com/auth/cloud-platform");
#pragma warning restore CS0618
		var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
		var baseUrl = $"https://identitytoolkit.googleapis.com/v1/projects/{ProjectId}";

		var smokeUids = new List<string>();
		string? pageToken = null;
		do
		{
			using var listRequest = new HttpRequestMessage(HttpMethod.Get,
				$"{baseUrl}/accounts:batchGet?maxResults=1000{(pageToken is null ? "" : $"&nextPageToken={pageToken}")}");
			listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using var listResponse = await Http.SendAsync(listRequest);
			listResponse.EnsureSuccessStatusCode();
			var page = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
			if (page.TryGetProperty("users", out var users))
			{
				smokeUids.AddRange(users.EnumerateArray()
					.Select(u => u.GetProperty("localId").GetString()!)
					.Where(id => id.StartsWith("smoke-test-", StringComparison.Ordinal)));
			}

			pageToken = page.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
		}
		while (!string.IsNullOrEmpty(pageToken));

		foreach (var smokeUid in smokeUids)
		{
			using var deleteRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/accounts:delete")
			{
				Content = JsonContent.Create(new { localId = smokeUid }),
			};
			deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using var deleteResponse = await Http.SendAsync(deleteRequest);
			Assert.True(deleteResponse.IsSuccessStatusCode,
				$"Cleanup of Auth user {smokeUid} failed: {await deleteResponse.Content.ReadAsStringAsync()}");
		}
	}

	private static GoogleCredential CreateStorageCredential()
	{
#pragma warning disable CS0618
		return GoogleCredential.FromFile(CredentialsPath())
			.CreateScoped("https://www.googleapis.com/auth/devstorage.read_write");
#pragma warning restore CS0618
	}

	private static async Task<HttpStatusCode> GetObjectMetadataStatusAsync(GoogleCredential credential, string objectName)
	{
		var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
		using var request = new HttpRequestMessage(HttpMethod.Get,
			$"https://storage.googleapis.com/storage/v1/b/{StorageBucket}/o/{Uri.EscapeDataString(objectName)}");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		using var response = await Http.SendAsync(request);
		return response.StatusCode;
	}

	internal static string CredentialsPath()
	{
		var fromEnv = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_PATH");
		if (!string.IsNullOrEmpty(fromEnv))
		{
			return fromEnv;
		}

		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, "src", "ZenlessLeague.Migrator", "secrets", "firebase-service-account.json");
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new InvalidOperationException("Firebase service-account file not found; set FIREBASE_CREDENTIALS_PATH.");
	}
}

public sealed class FirebaseLiveFactAttribute : FactAttribute
{
	public FirebaseLiveFactAttribute()
	{
		if (Environment.GetEnvironmentVariable("FIREBASE_LIVE_TESTS") != "1")
		{
			Skip = "Live Firebase test — set FIREBASE_LIVE_TESTS=1 to run.";
		}
	}
}
