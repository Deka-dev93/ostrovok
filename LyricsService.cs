using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ostrovok.Services;

public record LyricLine(TimeSpan Time, string Text);

/// <summary>Слова песен из LRCLIB (lrclib.net) — с таймкодами для «караоке».</summary>
public class LyricsService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static LyricsService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Ostrovok/1.0 (Windows)");

    public List<LyricLine> Lines { get; private set; } = new();
    public string Plain { get; private set; } = "";
    public bool Synced => Lines.Count > 0;
    string _key = "";

    public async Task LoadAsync(string title, string artist, double durationSec)
    {
        var key = artist + "|" + title;
        if (key == _key) return;
        _key = key; Lines = new(); Plain = "";
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist ?? "")}";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            JsonElement best = default; bool found = false; double bestDiff = double.MaxValue;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                bool hasSync = e.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String;
                double d = e.TryGetProperty("duration", out var dd) && dd.ValueKind == JsonValueKind.Number ? dd.GetDouble() : 0;
                double diff = durationSec > 0 ? Math.Abs(d - durationSec) : 0;
                if (!hasSync) diff += 1000;
                if (diff < bestDiff) { bestDiff = diff; best = e.Clone(); found = true; }
            }
            if (!found || key != _key) return;
            if (best.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String)
                Lines = ParseLrc(s.GetString());
            if (best.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String)
                Plain = p.GetString();
        }
        catch (Exception ex) { AppSettings.Log("lyrics: " + ex.Message); }
    }

    static readonly Regex Rx = new(@"\[(\d+):(\d+(?:\.\d+)?)\](.*)");
    static List<LyricLine> ParseLrc(string lrc)
    {
        var res = new List<LyricLine>();
        foreach (var line in lrc.Split('\n'))
        {
            var m = Rx.Match(line.Trim());
            if (!m.Success) continue;
            double min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double sec = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            res.Add(new LyricLine(TimeSpan.FromSeconds(min * 60 + sec), m.Groups[3].Value.Trim()));
        }
        return res;
    }

    public int IndexAt(TimeSpan t)
    {
        int idx = -1;
        for (int i = 0; i < Lines.Count; i++) { if (Lines[i].Time <= t) idx = i; else break; }
        return idx;
    }
}
