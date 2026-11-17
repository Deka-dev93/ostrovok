using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Ostrovok.Services;

/// <summary>Распознавание текста — локально (Windows OCR); перевод — Google, только по нажатию.</summary>
public static class OcrService
{
    public static async Task<string> RecognizeAsync(BitmapSource img)
    {
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);

            using var ras = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                w.WriteBytes(ms.ToArray());
                await w.StoreAsync();
                w.DetachStream();
            }
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            using var sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            // пробуем языки профиля пользователя, затем русский и английский
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ru"))
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (engine == null) return "";
            var res = await engine.RecognizeAsync(sb);
            return res.Text?.Trim() ?? "";
        }
        catch (Exception ex) { AppSettings.Log("ocr: " + ex.Message); return ""; }
    }
}

public static class TranslateService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Русский → английский, всё остальное → русский.</summary>
    public static string PickTarget(string text)
    {
        int cyr = text.Count(c => c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё');
        int letters = text.Count(char.IsLetter);
        return letters > 0 && cyr * 2 > letters ? "en" : "ru";
    }

    public static async Task<string> TranslateAsync(string text, string target = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        target ??= PickTarget(text);
        try
        {
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + target +
                      "&dt=t&q=" + Uri.EscapeDataString(text);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var sb = new StringBuilder();
            foreach (var seg in doc.RootElement[0].EnumerateArray())
                sb.Append(seg[0].GetString());
            return sb.ToString();
        }
        catch (Exception ex) { AppSettings.Log("translate: " + ex.Message); return "⚠ Не удалось перевести (нет сети?)"; }
    }
}
