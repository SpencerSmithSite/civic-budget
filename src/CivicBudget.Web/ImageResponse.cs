namespace CivicBudget.Web;

/// <summary>
/// Headers for serving an uploaded image from this site's own origin. The upload services already
/// accept only PNG, JPEG, and WebP whose bytes match; these make sure a browser treats the bytes as
/// that image and nothing else, even if a bad file ever got stored.
/// </summary>
public static class ImageResponse
{
    public static void Harden(HttpResponse response)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
    }
}
