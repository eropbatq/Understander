namespace Understander.Services;

public class OcrService
{
    /// <summary>
    /// Reads text from an image. Uses platform-native OCR where available.
    /// </summary>
    public async Task<string> ReadTextAsync(FileResult file)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

#if IOS || MACCATALYST
            return await ReadTextWithVisionAsync(bytes);
#elif ANDROID
            return await ReadTextWithMLKitAsync(bytes);
#else
            return await ReadTextFallbackAsync(bytes);
#endif
        }
        catch
        {
            return "";
        }
    }

#if IOS || MACCATALYST
    private static Task<string> ReadTextWithVisionAsync(byte[] bytes)
    {
        // iOS: Vision framework – placeholder
        return Task.FromResult("(OCR on iOS requires native integration)");
    }
#elif ANDROID
    private static Task<string> ReadTextWithMLKitAsync(byte[] bytes)
    {
        // Android: ML Kit – placeholder
        return Task.FromResult("(OCR on Android requires ML Kit integration)");
    }
#endif

    private static Task<string> ReadTextFallbackAsync(byte[] bytes)
    {
        // Fallback: integrate Tesseract via NuGet for cross-platform OCR
        return Task.FromResult("(OCR not available on this platform)");
    }
}
