using ZenlessLeague.Api.Features.Storage;

namespace ZenlessLeague.Api.Tests;

// Records calls and returns a deterministic fake URL instead of hitting real Firebase Storage.
public class FakeFirebaseStorageClient : IFirebaseStorageClient
{
	public List<(string Base64Image, string PathPrefix)> Uploads { get; } = [];
	public List<string> Deletes { get; } = [];

	public Task<string> UploadImageAsync(string base64Image, string pathPrefix, CancellationToken ct = default)
	{
		Uploads.Add((base64Image, pathPrefix));
		return Task.FromResult($"https://fake-storage.example/{pathPrefix}.jpg");
	}

	public Task DeleteImageAsync(string pathPrefix, CancellationToken ct = default)
	{
		Deletes.Add(pathPrefix);
		return Task.CompletedTask;
	}
}
