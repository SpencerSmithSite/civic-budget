using CivicBudget.Application.Common;

namespace CivicBudget.Application.Tests;

/// <summary>Uploaded images must be raster images whose bytes match what they claim to be.</summary>
public class UploadedImageTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 0];

    [Fact]
    public void Accepts_png_jpeg_and_webp_that_match_their_type()
    {
        Assert.True(UploadedImage.Validate(PngBytes, "image/png", "logo").IsSuccess);
        Assert.True(UploadedImage.Validate(JpegBytes, "image/jpeg", "logo").IsSuccess);
        Assert.True(UploadedImage.Validate(WebpBytes, "image/webp", "logo").IsSuccess);
    }

    [Fact]
    public void Refuses_svg_even_when_labelled_as_one()
    {
        byte[] svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        Assert.True(UploadedImage.Validate(svg, "image/svg+xml", "logo").IsFailure);
        Assert.True(UploadedImage.Validate(svg, "image/png", "logo").IsFailure);
    }

    [Fact]
    public void Refuses_bytes_that_do_not_match_the_declared_type()
    {
        Assert.True(UploadedImage.Validate(JpegBytes, "image/png", "picture").IsFailure);
        Assert.True(UploadedImage.Validate("<html>"u8.ToArray(), "image/jpeg", "picture").IsFailure);
    }

    [Fact]
    public void Refuses_empty_and_oversized_files()
    {
        Assert.True(UploadedImage.Validate([], "image/png", "picture").IsFailure);
        byte[] big = new byte[UploadedImage.MaxBytes + 1];
        PngBytes.CopyTo(big, 0);
        Assert.True(UploadedImage.Validate(big, "image/png", "picture").IsFailure);
    }
}
