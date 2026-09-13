using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZenlessLeague.Api.Data;

public interface IFirebaseCustomTokenSigner
{
	// Mints a Firebase "custom token" for the given uid, for the frontend to exchange via
	// signInWithCustomToken. Firebase auto-provisions the Auth user record on first use of an
	// unseen uid, so this is the only Firebase-side step needed for a brand new account.
	string CreateCustomToken(string uid);
}

// A Firebase custom token is just a self-signed RS256 JWT per Firebase's documented format — no
// Google API call is involved in minting one. Signed with the same service account credentials
// already used for FirebaseStorageClient (see FirebaseCredentials); only client_email/private_key
// are needed here, so this parses the JSON directly instead of pulling in the full FirebaseAdmin
// SDK for one operation.
//
// Built by hand (header/payload/signature) rather than via JwtSecurityToken/JwtPayload — Firebase
// is strict about the exact claim set (iss/sub/aud/iat/exp/uid, nothing more, nothing missing),
// and letting a general-purpose JWT library decide which claims to auto-populate produced a token
// Firebase rejected as INVALID_CUSTOM_TOKEN. This mirrors exactly what the Firebase Admin SDK's
// own createCustomToken does internally.
public class FirebaseCustomTokenSigner : IFirebaseCustomTokenSigner
{
	private const string IdentityToolkitAudience =
		"https://identitytoolkit.googleapis.com/google.identity.identitytoolkit.v1.IdentityToolkit";

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

	private readonly Lazy<(string ClientEmail, RSA PrivateKey)> credentials;

	public FirebaseCustomTokenSigner(IConfiguration configuration)
	{
		credentials = new Lazy<(string, RSA)>(() =>
		{
			using var document = JsonDocument.Parse(FirebaseCredentials.ResolveJson(configuration));
			var root = document.RootElement;
			var clientEmail = root.GetProperty("client_email").GetString()
				?? throw new InvalidOperationException("Firebase service account file is missing client_email.");
			var privateKeyPem = root.GetProperty("private_key").GetString()
				?? throw new InvalidOperationException("Firebase service account file is missing private_key.");

			var rsa = RSA.Create();
			rsa.ImportFromPem(privateKeyPem);
			return (clientEmail, rsa);
		});
	}

	public string CreateCustomToken(string uid)
	{
		var (clientEmail, privateKey) = credentials.Value;
		var now = DateTimeOffset.UtcNow;

		var header = new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT" };
		var payload = new Dictionary<string, object>
		{
			["iss"] = clientEmail,
			["sub"] = clientEmail,
			["aud"] = IdentityToolkitAudience,
			["iat"] = now.ToUnixTimeSeconds(),
			["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
			["uid"] = uid,
		};

		var signingInput = $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions))}." +
			$"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))}";

		var signature = privateKey.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

		return $"{signingInput}.{Base64UrlEncode(signature)}";
	}

	private static string Base64UrlEncode(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
