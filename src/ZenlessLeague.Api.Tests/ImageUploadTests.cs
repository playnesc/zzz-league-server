using System.Text;
using ZenlessLeague.Api.Features.Storage;

namespace ZenlessLeague.Api.Tests;

public class ImageUploadTests
{
	private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
	private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];

	private static string DataUrl(string mediaType, byte[] bytes) => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

	private static string DataUrl(string mediaType, string text) => DataUrl(mediaType, Encoding.UTF8.GetBytes(text));

	[Fact]
	public void Decode_Png_UsesSniffedTypeAndExtension()
	{
		var image = ImageUpload.Decode(DataUrl("image/png", Png));

		Assert.Equal("image/png", image.ContentType);
		Assert.Equal("png", image.Extension);
	}

	[Fact]
	public void Decode_MislabelledRaster_IsStoredAsWhatItReallyIs()
	{
		// Browsers sometimes mislabel; the bytes decide what's stored.
		var image = ImageUpload.Decode(DataUrl("image/png", Jpeg));

		Assert.Equal("image/jpeg", image.ContentType);
	}

	[Fact]
	public void Decode_WithoutDataUrlPrefix_SniffsType()
	{
		var image = ImageUpload.Decode(Convert.ToBase64String(Png));

		Assert.Equal("image/png", image.ContentType);
	}

	[Fact]
	public void Decode_PlainSvgLogo_IsAccepted()
	{
		var image = ImageUpload.Decode(DataUrl("image/svg+xml", """<svg xmlns="http://www.w3.org/2000/svg"><circle r="4"/></svg>"""));

		Assert.Equal("image/svg+xml", image.ContentType);
		Assert.Equal("svg", image.Extension);
	}

	[Theory]
	[InlineData("text/html", "<html><body>phish</body></html>")]
	[InlineData("image/png", "<html><body>phish</body></html>")] // HTML disguised as an image
	[InlineData("image/svg+xml", """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""")]
	[InlineData("image/svg+xml", """<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)">x</a></svg>""")]
	[InlineData("image", "x")] // malformed media type used to crash with IndexOutOfRangeException
	public void Decode_DisallowedContent_Throws(string mediaType, string content)
	{
		Assert.Throws<FirebaseStorageException>(() => ImageUpload.Decode(DataUrl(mediaType, content)));
	}

	[Fact]
	public void Decode_SvgDeclaredAsRaster_Throws()
	{
		// Declaring a raster type while sending SVG text would otherwise slip SVG past a raster-only check.
		Assert.Throws<FirebaseStorageException>(() =>
			ImageUpload.Decode(DataUrl("image/png", """<svg xmlns="http://www.w3.org/2000/svg"></svg>""")));
	}

	[Fact]
	public void Decode_TooLarge_Throws()
	{
		var big = new byte[ImageUpload.MaxImageSizeBytes + 1];
		Png.CopyTo(big, 0);

		Assert.Throws<FirebaseStorageException>(() => ImageUpload.Decode(DataUrl("image/png", big)));
	}

	[Fact]
	public void Decode_InvalidBase64_Throws()
	{
		Assert.Throws<FirebaseStorageException>(() => ImageUpload.Decode("data:image/png;base64,not base64!!"));
	}
}
