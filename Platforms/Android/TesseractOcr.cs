using System.Diagnostics;
using Android.Graphics;
using Com.Googlecode.Tesseract.Android;
using Microsoft.Maui.Storage;

namespace Understander.Services;

/// <summary>
/// Офлайн-OCR на движке Tesseract (Tesseract4Android).
///
/// Зачем: ML Kit Text Recognition v2 (его использует Plugin.Maui.OCR)
/// поддерживает только Latin / Chinese / Devanagari / Japanese / Korean
/// и физически не умеет кириллицу. Поэтому русский текст распознаётся здесь.
///
/// AAR подключается в csproj как обычная Android-библиотека
/// (libs/tesseract4android-4.9.0.aar) — C#-биндинги генерируются при сборке.
///
/// Модели (*.traineddata) лежат в Resources/Raw/tessdata и при первом запуске
/// копируются в приватную папку приложения (на Android 10+ Tesseract может
/// читать данные только оттуда).
/// </summary>
internal static class TesseractOcr
{
    /// <summary>Модели, которые поставляются в APK (Resources/Raw/tessdata).</summary>
    static readonly string[] BundledModels = { "rus", "eng" };

    /// <summary>ISO 639-1 → имя модели Tesseract.</summary>
    static readonly Dictionary<string, string> ModelByIso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "rus",
        ["uk"] = "ukr",
        ["bg"] = "bul",
        ["sr"] = "srp",
        ["be"] = "bel",
        ["mk"] = "mkd",
        ["kk"] = "kaz",
    };

    // TessBaseAPI не потокобезопасен, поэтому распознавание сериализуем.
    static readonly SemaphoreSlim Gate = new(1, 1);
    static string? _dataPath;

    /// <summary>Поддерживается ли язык офлайн-движком (кириллица).</summary>
    public static bool SupportsLanguage(string? bcp47)
    {
        if (string.IsNullOrWhiteSpace(bcp47) || bcp47.Length < 2) return false;
        return ModelByIso.ContainsKey(bcp47[..2]);
    }

    /// <summary>Распознаёт текст. Возвращает пустую строку, если ничего не нашлось.</summary>
    public static async Task<string> RecognizeAsync(byte[] imageBytes, string bcp47)
    {
        try
        {
            var languages = ResolveLanguageString(bcp47);

            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var dataPath = _dataPath ??= await CopyModelsToPrivateStorageAsync().ConfigureAwait(false);

                // Tesseract — тяжёлая синхронная работа, уводим с UI-потока.
                return await Task.Run(() => RecognizeCore(imageBytes, languages, dataPath)).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception ex)
        {
            // Не роняем OCR: вызывающая сторона просто попробует ML Kit.
            Debug.WriteLine($"[Tesseract] Ошибка: {ex}");
            return "";
        }
    }

    /// <summary>Собирает строку вида "rus+eng" (язык + латиница для смешанного текста).</summary>
    static string ResolveLanguageString(string bcp47)
    {
        var iso = bcp47[..2];
        var model = ModelByIso.TryGetValue(iso, out var m) ? m : "rus";

        if (!BundledModels.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[Tesseract] Модель '{model}' не входит в APK, используем 'rus'");
            model = "rus";
        }

        return BundledModels.Contains("eng", StringComparer.OrdinalIgnoreCase) && model != "eng"
            ? $"{model}+eng"
            : model;
    }

    /// <summary>
    /// Копирует *.traineddata из APK (MauiAsset) в приватную папку приложения.
    /// Возвращает путь для TessBaseAPI.init (внутри него Tesseract ищет "tessdata").
    /// </summary>
    static async Task<string> CopyModelsToPrivateStorageAsync()
    {
        var root = System.IO.Path.Combine(FileSystem.CacheDirectory, "tesseract");
        var tessdata = System.IO.Path.Combine(root, "tessdata");
        Directory.CreateDirectory(tessdata);

        foreach (var model in BundledModels)
        {
            var target = System.IO.Path.Combine(tessdata, model + ".traineddata");
            if (File.Exists(target) && new FileInfo(target).Length > 0)
                continue;

            await using var source = await FileSystem
                .OpenAppPackageFileAsync($"tessdata/{model}.traineddata")
                .ConfigureAwait(false);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination).ConfigureAwait(false);

            Debug.WriteLine($"[Tesseract] Скопирована модель {model}.traineddata ({new FileInfo(target).Length} байт)");
        }

        return root;
    }

    static string RecognizeCore(byte[] imageBytes, string languages, string dataPath)
    {
        Bitmap? bitmap = null;
        TessBaseAPI? api = null;

        try
        {
            bitmap = DecodeAndPrepare(imageBytes);
            if (bitmap is null)
            {
                Debug.WriteLine("[Tesseract] Не удалось декодировать изображение");
                return "";
            }

            api = new TessBaseAPI();
            if (!api.Init(dataPath, languages))
            {
                Debug.WriteLine($"[Tesseract] init() failed (path={dataPath}, lang={languages})");
                return "";
            }

            // PSM_AUTO — универсальный режим для фото вывесок и документов.
            api.SetPageSegMode(TessBaseAPI.PageSegMode.PsmAuto);
            api.SetImage(bitmap);

            var text = api.UTF8Text ?? "";
            Debug.WriteLine($"[Tesseract] lang={languages} length={text.Length} confidence={api.MeanConfidence()}");

            return text.Trim();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Tesseract] Ошибка распознавания: {ex}");
            return "";
        }
        finally
        {
            api?.Recycle();
            api?.Dispose();
            bitmap?.Recycle();
            bitmap?.Dispose();
        }
    }

    /// <summary>Декодирует картинку и при необходимости уменьшает её до 2048 px по длинной стороне.</summary>
    static Bitmap? DecodeAndPrepare(byte[] imageBytes)
    {
        var bitmap = BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length);
        if (bitmap is null) return null;

        const int maxSide = 2048;
        var longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest <= maxSide) return bitmap;

        var scale = (float)maxSide / longest;
        var scaled = Bitmap.CreateScaledBitmap(
            bitmap,
            Math.Max(1, (int)(bitmap.Width * scale)),
            Math.Max(1, (int)(bitmap.Height * scale)),
            filter: true);

        if (scaled is null || scaled == bitmap) return bitmap;

        bitmap.Recycle();
        bitmap.Dispose();
        return scaled;
    }
}