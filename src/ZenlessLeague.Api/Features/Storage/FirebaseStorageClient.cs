using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Storage;

// Covers both bad client input (invalid base64, oversized file) and genuine upstream failures
// (Firebase Storage itself erroring) — treated uniformly as 400 rather than splitting into two
// exception types, since the caller can't act on the distinction either way.
public class FirebaseStorageException(string message) : ValidationApiException(message);

public interface IFirebaseStorageClient
{
	// Decodes a data-url base64 image, enforces the 3MB cap, deletes any previously uploaded
	// file(s) under the same prefix (so re-uploading with a different extension doesn't leave
	// orphans), uploads, and returns the public URL.
	Task<string> UploadImageAsync(string base64Image, string pathPrefix, CancellationToken ct = default);

	// Deletes any uploaded file(s) under the given prefix without replacing them — used when the
	// owning record itself is deleted (e.g. a team's logo), not just re-uploaded.
	Task DeleteImageAsync(string pathPrefix, CancellationToken ct = default);
}

// REST client for the Firebase/Google Cloud Storage JSON API, using a GoogleCredential
// bearer-token. Verified against the real zzz-league.firebasestorage.app bucket.
public partial class FirebaseStorageClient(HttpClient httpClient, Lazy<GoogleCredential> credential, IOptions<FirebaseStorageOptions> options) : IFirebaseStorageClient
{
	private const long MaxImageSizeBytes = 3 * 1024 * 1024;
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	// Matches any "data:<media-type>;base64," prefix, not just plain-word media types -- browsers
	// report some image types with characters \w doesn't cover (e.g. SVG as "image/svg+xml"), and
	// falling back to a made-up content type while leaving the real prefix stuck to the payload
	// broke every such upload (the stray "data:image/svg+xml;base64," text isn't valid base64).
	[GeneratedRegex(@"^data:([^;]+);base64,")]
	private static partial Regex DataUrlPattern();

	public async Task<string> UploadImageAsync(string base64Image, string pathPrefix, CancellationToken ct = default)
	{
		var match = DataUrlPattern().Match(base64Image);
		var contentType = match.Success ? match.Groups[1].Value : "image/jpeg";
		var ext = contentType.Split('/')[1].Split('+')[0];

		var base64Data = DataUrlPattern().Replace(base64Image, string.Empty);
		byte[] buffer;
		try
		{
			buffer = Convert.FromBase64String(base64Data);
		}
		catch (FormatException)
		{
			throw new FirebaseStorageException("Invalid base64 image data.");
		}

		if (buffer.Length > MaxImageSizeBytes)
		{
			throw new FirebaseStorageException("File too large, max 3MB");
		}

		var bucket = options.Value.StorageBucket ?? throw new InvalidOperationException("Firebase:StorageBucket is not configured.");
		var filePath = $"{pathPrefix}.{ext}";

		await DeleteByPrefixAsync(bucket, $"{pathPrefix}.", ct);
		await UploadAsync(bucket, filePath, buffer, contentType, ct);

		return $"https://storage.googleapis.com/{bucket}/{filePath}";
	}

	public async Task DeleteImageAsync(string pathPrefix, CancellationToken ct = default)
	{
		var bucket = options.Value.StorageBucket ?? throw new InvalidOperationException("Firebase:StorageBucket is not configured.");
		await DeleteByPrefixAsync(bucket, $"{pathPrefix}.", ct);
	}

	private async Task DeleteByPrefixAsync(string bucket, string prefix, CancellationToken ct)
	{
		var token = await GetAccessTokenAsync(ct);

		using var listRequest = new HttpRequestMessage(HttpMethod.Get,
			$"https://storage.googleapis.com/storage/v1/b/{Uri.EscapeDataString(bucket)}/o?prefix={Uri.EscapeDataString(prefix)}");
		listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var listResponse = await httpClient.SendAsync(listRequest, ct);
		listResponse.EnsureSuccessStatusCode();

		var listing = await listResponse.Content.ReadFromJsonAsync<GcsObjectList>(JsonOptions, ct);
		if (listing?.Items is null)
		{
			return;
		}

		foreach (var item in listing.Items)
		{
			using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
				$"https://storage.googleapis.com/storage/v1/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(item.Name)}");
			deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var deleteResponse = await httpClient.SendAsync(deleteRequest, ct);
			if (deleteResponse.StatusCode != HttpStatusCode.NotFound)
			{
				deleteResponse.EnsureSuccessStatusCode();
			}
		}
	}

	private async Task UploadAsync(string bucket, string filePath, byte[] buffer, string contentType, CancellationToken ct)
	{
		var token = await GetAccessTokenAsync(ct);

		var url = $"https://storage.googleapis.com/upload/storage/v1/b/{Uri.EscapeDataString(bucket)}/o" +
				  $"?uploadType=media&name={Uri.EscapeDataString(filePath)}&predefinedAcl=publicRead";

		using var request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Content = new ByteArrayContent(buffer);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

		using var response = await httpClient.SendAsync(request, ct);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(ct);
			throw new FirebaseStorageException($"Firebase Storage upload failed: {body}");
		}
	}

	private async Task<string> GetAccessTokenAsync(CancellationToken ct) =>
		await credential.Value.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

	private record GcsObjectList(List<GcsObject>? Items);
	private record GcsObject(string Name);
}
