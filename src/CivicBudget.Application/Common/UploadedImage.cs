namespace CivicBudget.Application.Common;

/// <summary>
/// The one rule for images people upload (profile pictures, the government's logo). The browser
/// resizes them to PNG before they are sent, but the server cannot rely on that: a hand-built
/// request can send any bytes with any label, and these images are served back from this site's
/// own origin. So only raster formats are accepted (SVG can carry script), and the first bytes
/// must match the declared type.
/// </summary>
public static class UploadedImage
{
    public const int MaxBytes = 512 * 1024;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    /// <param name="what">What the image is, for the message ("picture", "logo").</param>
    public static Result Validate(byte[] data, string contentType, string what)
    {
        if (data.Length == 0 || data.Length > MaxBytes)
        {
            return Result.Failure($"The {what} must be under {MaxBytes / 1024} KB.");
        }

        return MatchesSignature(data, contentType)
            ? Result.Success()
            : Result.Failure($"That file is not a PNG, JPEG, or WebP image. Choose a {what} in one of those formats.");
    }

    public static bool MatchesSignature(ReadOnlySpan<byte> data, string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => data.StartsWith(Png),
        "image/jpeg" => data.StartsWith(Jpeg),
        "image/webp" => data.Length >= 12 && data.StartsWith(Riff) && data[8..12].SequenceEqual(Webp),
        _ => false,
    };
}
