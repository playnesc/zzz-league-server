using System.Text;
using System.Text.RegularExpressions;

namespace ZenlessLeague.Api.Features.Storage;

public record DecodedImage(byte[] Bytes, string ContentType, string Extension);

// Decodes and validates a user-supplied base64 / data-url image before it goes to the public
// bucket. Only real images of an allowlisted type are accepted, and the type comes from the file's
// own bytes rather than the client-declared media type, so e.g. "data:text/html;base64,..." (or HTML
// labelled image/png) can't be hosted under the league's name.
public static partial class ImageUpload
{
	public const long MaxImageSizeBytes = 3 * 1024 * 1024;

	// Content type -> stored file extension. SVG is kept for team logos.
	private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		["image/png"] = "png",
		["image/jpeg"] = "jpeg",
		["image/webp"] = "webp",
		["image/gif"] = "gif",
		["image/avif"] = "avif",
		["image/svg+xml"] = "svg",
	};

	// Matches any "data:<media-type>;base64," prefix, not just plain-word media types -- browsers
	// report some image types with characters \w doesn't cover (e.g. SVG as "image/svg+xml").
	[GeneratedRegex(@"^data:([^;]+);base64,")]
	private static partial Regex DataUrlPattern();

	public static DecodedImage Decode(string base64Image)
	{
		var match = DataUrlPattern().Match(base64Image);
		var declaredType = match.Success ? match.Groups[1].Value.Trim() : null;
		if (declaredType is not null && !AllowedTypes.ContainsKey(declaredType))
		{
			throw new FirebaseStorageException("Unsupported image type — use PNG, JPEG, WebP, GIF, AVIF or SVG.");
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(match.Success ? base64Image[match.Length..] : base64Image);
		}
		catch (FormatException)
		{
			throw new FirebaseStorageException("Invalid base64 image data.");
		}

		if (bytes.Length > MaxImageSizeBytes)
		{
			throw new FirebaseStorageException("File too large, max 3MB");
		}

		var actualType = SniffType(bytes)
			?? throw new FirebaseStorageException("That file isn't a supported image (PNG, JPEG, WebP, GIF, AVIF or SVG).");

		// A declared type that disagrees with the bytes is only tolerated between raster formats
		// (browsers sometimes mislabel); claiming to be a raster image while actually being SVG/text,
		// or vice versa, is rejected.
		if (declaredType is not null && IsSvg(declaredType) != IsSvg(actualType))
		{
			throw new FirebaseStorageException("The image's contents don't match its declared type.");
		}

		return new DecodedImage(bytes, actualType, AllowedTypes[actualType]);
	}

	private static bool IsSvg(string contentType) => contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

	private static string? SniffType(byte[] b)
	{
		if (b.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
		{
			return "image/png";
		}

		if (b.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
		{
			return "image/jpeg";
		}

		if (b.AsSpan().StartsWith("GIF87a"u8) || b.AsSpan().StartsWith("GIF89a"u8))
		{
			return "image/gif";
		}

		if (b.Length >= 12 && b.AsSpan().StartsWith("RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8))
		{
			return "image/webp";
		}

		// ISO-BMFF "ftyp" box with an AVIF brand (still image or image sequence).
		if (b.Length >= 12 && b.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
			(b.AsSpan(8, 4).SequenceEqual("avif"u8) || b.AsSpan(8, 4).SequenceEqual("avis"u8)))
		{
			return "image/avif";
		}

		return LooksLikeSafeSvg(b) ? "image/svg+xml" : null;
	}

	// An SVG is XML text containing an <svg> root. Scripted SVGs are refused — the bucket is public,
	// and an SVG opened directly runs its scripts. (A cheap guard, not a full sanitizer.)
	private static bool LooksLikeSafeSvg(byte[] bytes)
	{
		string text;
		try
		{
			text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			return false;
		}

		var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
		return trimmed.StartsWith('<')
			&& text.Contains("<svg", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("<script", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("<foreignObject", StringComparison.OrdinalIgnoreCase);
	}
}
