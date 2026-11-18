using System.Diagnostics;
using Plugin.Maui.OCR;

namespace Understander.Services;

public class OcrService
{
    private readonly IOcrService _ocrService;

    public OcrService(IOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    public async Task<string> ReadTextAsync(FileResult file)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            Debug.WriteLine($"[OCR] Image size: {bytes.Length} bytes");

#if WINDOWS
            return await ReadTextWithWindowsOcrAsync(bytes);
#else
            // Try Russian first
            var options = new OcrOptions.Builder()
                .SetLanguage("ru")
                .SetTryHard(true)
                .Build();

            var result = await _ocrService.RecognizeTextAsync(bytes, options);

            Debug.WriteLine($"[OCR] Result: '{result?.AllText ?? "null"}'");
            Debug.WriteLine($"[OCR] AllText length: {result?.AllText?.Length ?? 0}");
            Debug.WriteLine($"[OCR] Success: {result?.Success}");

            if (string.IsNullOrWhiteSpace(result?.AllText))
                return "Текст не найден на изображении";

            return result.AllText.Trim();
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OCR] Error: {ex.Message}");
            return $"Ошибка OCR: {ex.Message}";
        }
    }

#if WINDOWS
    private static async Task<string> ReadTextWithWindowsOcrAsync(byte[] bytes)
    {
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            stream.Seek(0);

            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var bitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ru-RU"));
            if (ocrEngine == null)
                ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (ocrEngine == null)
                ocrEngine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("de-DE"));
            if (ocrEngine == null)
                return "OCR движок недоступен";

            var result = await ocrEngine.RecognizeAsync(bitmap);
            Debug.WriteLine($"[OCR Windows] Result: '{result.Text}'");
            return result.Text?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OCR Windows] Error: {ex.Message}");
            return $"Ошибка OCR: {ex.Message}";
        }
    }
#endif
}
