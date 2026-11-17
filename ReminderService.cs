using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Ostrovok.Services;

public record CalEvent(DateTime Start, string Title);

/// <summary>Напоминания словами («18:30 созвон», «завтра 9:00 врач», «через 20 мин чай») + календарь по ICS-ссылке.</summary>
public class ReminderService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public List<CalEvent> IcsEvents { get; private set; } = new();
    public event Action<string, string> Fire;      // (заголовок, текст)
    public event Action Changed;
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly HashSet<string> _warned = new();

    public ReminderService() { _t.Tick += (_, _) => Check(); _t.Start(); }

    // ───────── разбор текста ─────────
    static readonly Regex RxIn = new(@"^(?:через|in)\s+(\d+)\s*(мин\w*|min\w*|m|час\w*|h\w*|hour\w*|сек\w*|sec\w*|s)\s*(.*)$", RegexOptions.IgnoreCase);
    static readonly Regex RxDay = new(@"^(завтра|послезавтра|сегодня|tomorrow|today)\s+(\d{1,2})[:.](\d{2})\s*(.*)$", RegexOptions.IgnoreCase);
    static readonly Regex RxTime = new(@"^(\d{1,2})[:.](\d{2})\s*(.*)$");

    public static bool TryParse(string input, out DateTime when, out string text)
    {
        when = default; text = "";
        input = input.Trim();
        var now = DateTime.Now;

        var m = RxIn.Match(input);
        if (m.Success)
        {
            int n = int.Parse(m.Groups[1].Value);
            string u = m.Groups[2].Value.ToLowerInvariant();
            bool isSec = u.StartsWith("сек") || u.StartsWith("s");
            bool isHour = u.StartsWith("час") || u.StartsWith("h");
            when = isSec ? now.AddSeconds(n) : isHour ? now.AddHours(n) : now.AddMinutes(n);
            text = m.Groups[3].Value.Trim();
            return true;
        }
        m = RxDay.Match(input);
        if (m.Success)
        {
            string d = m.Groups[1].Value.ToLowerInvariant();
            int add = d is "завтра" or "tomorrow" ? 1 : d == "послезавтра" ? 2 : 0;
            if (!TryHm(m.Groups[2].Value, m.Groups[3].Value, out int h, out int mi)) return false;
            when = now.Date.AddDays(add).AddHours(h).AddMinutes(mi);
            text = m.Groups[4].Value.Trim();
            return true;
        }
        m = RxTime.Match(input);
        if (m.Success)
        {
            if (!TryHm(m.Groups[1].Value, m.Groups[2].Value, out int h, out int mi)) return false;
            when = now.Date.AddHours(h).AddMinutes(mi);
            if (when <= now) when = when.AddDays(1);
            text = m.Groups[3].Value.Trim();
            return true;
        }
        return false;
    }

    static bool TryHm(string hs, string ms, out int h, out int m)
    {
        h = int.Parse(hs); m = int.Parse(ms);
        return h is >= 0 and < 24 && m is >= 0 and < 60;
    }

    public bool Add(string input)
    {
        if (!TryParse(input, out var when, out var text)) return false;
        AppSettings.Current.Reminders.Add(new ReminderItem { When = when, Text = string.IsNullOrWhiteSpace(text) ? "Напоминание" : text });
        AppSettings.Current.Save();
        Changed?.Invoke();
        return true;
    }

    public void Remove(ReminderItem r)
    {
        AppSettings.Current.Reminders.Remove(r);
        AppSettings.Current.Save();
        Changed?.Invoke();
    }

    // ───────── проверка ─────────
    void Check()
    {
        var now = DateTime.Now;
        bool dirty = false;
        foreach (var r in AppSettings.Current.Reminders.ToList())
        {
            if (!r.Fired && r.When <= now)
            {
                r.Fired = true; dirty = true;
                Fire?.Invoke("🔔 Напоминание", r.Text);
            }
        }
        AppSettings.Current.Reminders.RemoveAll(r => r.Fired && r.When < now.AddDays(-1));
        foreach (var e in IcsEvents)
        {
            var left = e.Start - now;
            string key = e.Start.Ticks + e.Title;
            if (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(10) && _warned.Add(key))
                Fire?.Invoke("📅 " + e.Title, $"через {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} мин");
        }
        if (dirty) { AppSettings.Current.Save(); Changed?.Invoke(); }
    }

    public CalEvent NextEvent() =>
        IcsEvents.Where(e => e.Start > DateTime.Now.AddMinutes(-1)).OrderBy(e => e.Start).FirstOrDefault();

    // ───────── ICS (Google / Outlook / Яндекс) ─────────
    public async Task LoadIcsAsync()
    {
        var url = AppSettings.Current.IcsUrl;
        if (string.IsNullOrWhiteSpace(url)) { IcsEvents = new(); Changed?.Invoke(); return; }
        try
        {
            var raw = await Http.GetStringAsync(url);
            IcsEvents = ParseIcs(raw);
            Changed?.Invoke();
        }
        catch (Exception ex) { AppSettings.Log("ics: " + ex.Message); }
    }

    static List<CalEvent> ParseIcs(string raw)
    {
        raw = Regex.Replace(raw, @"\r?\n[ \t]", "");        // разворачиваем перенесённые строки
        var res = new List<CalEvent>();
        var from = DateTime.Now.AddDays(-1); var to = DateTime.Now.AddDays(30);
        foreach (Match ev in Regex.Matches(raw, @"BEGIN:VEVENT(.*?)END:VEVENT", RegexOptions.Singleline))
        {
            string body = ev.Groups[1].Value;
            var sum = Regex.Match(body, @"^SUMMARY[^:]*:(.*)$", RegexOptions.Multiline).Groups[1].Value.Trim()
                .Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ");
            var ds = Regex.Match(body, @"^DTSTART([^:]*):(\S+)", RegexOptions.Multiline);
            if (!ds.Success) continue;
            if (!TryParseIcsDate(ds.Groups[2].Value.Trim(), out var start)) continue;
            // повторяющиеся события (RRULE) не раскрываем — показываем только ближайшее вхождение из файла
            if (start >= from && start <= to) res.Add(new CalEvent(start, sum.Length == 0 ? "Событие" : sum));
        }
        return res.OrderBy(e => e.Start).ToList();
    }

    static bool TryParseIcsDate(string s, out DateTime dt)
    {
        dt = default;
        string[] fmts = { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd" };
        if (!DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture,
                s.EndsWith("Z") ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.None, out dt))
            return false;
        if (s.EndsWith("Z")) dt = dt.ToLocalTime();
        return true;
    }
}
