using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Ostrovok.Services;

public record CalEvent(DateTime Start, string Title, bool AllDay = false, DateTime? End = null);

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
            if (!e.AllDay && left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(10) && _warned.Add(key))
                Fire?.Invoke("📅 " + e.Title, $"через {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} мин");
        }
        if (dirty) { AppSettings.Current.Save(); Changed?.Invoke(); }
    }

    public CalEvent NextEvent() =>
        IcsEvents.Where(e => e.Start > DateTime.Now.AddMinutes(-1)).OrderBy(e => e.Start).FirstOrDefault();

    // ───────── ICS (Google / Outlook / Яндекс) ─────────
    public int IcsCount => IcsEvents.Count;

    /// <summary>Загружает календарь по ссылке. Возвращает null при успехе или текст ошибки.</summary>
    public async Task<string> LoadIcsAsync()
    {
        var url = (AppSettings.Current.IcsUrl ?? "").Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        if (url.Length == 0) { IcsEvents = new(); Changed?.Invoke(); return null; }
        try
        {
            var raw = await Http.GetStringAsync(url);
            if (!raw.Contains("BEGIN:VCALENDAR")) return "По этой ссылке не календарь. Нужен «Секретный адрес в формате iCal».";
            IcsEvents = await Task.Run(() => ParseIcs(raw));
            Changed?.Invoke();
            return null;
        }
        catch (HttpRequestException ex)
        {
            AppSettings.Log("ics: " + ex.Message);
            return ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden
                ? "Ссылка не подошла (доступ закрыт или адрес изменился). Скопируйте «Секретный адрес в формате iCal» заново."
                : "Не удалось загрузить календарь. Проверьте интернет и ссылку.";
        }
        catch (Exception ex) { AppSettings.Log("ics: " + ex.Message); return "Не удалось загрузить календарь."; }
    }

    static string Unesc(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    static Dictionary<string, List<(string par, string val)>> Props(string body)
    {
        var d = new Dictionary<string, List<(string par, string val)>>();
        foreach (Match m in Regex.Matches(body, @"^([A-Za-z-]+)((?:;[^:\r\n]*)?):(.*?)\r?$", RegexOptions.Multiline))
        {
            var k = m.Groups[1].Value.ToUpperInvariant();
            if (!d.TryGetValue(k, out var l)) { l = new(); d[k] = l; }
            l.Add((m.Groups[2].Value, m.Groups[3].Value.Trim()));
        }
        return d;
    }

    static List<CalEvent> ParseIcs(string raw)
    {
        raw = Regex.Replace(raw, @"\r?\n[ \t]", "");        // разворачиваем перенесённые строки
        var res = new List<CalEvent>();
        var from = DateTime.Today.AddDays(-62); var to = DateTime.Today.AddDays(400);

        var evs = new List<Dictionary<string, List<(string par, string val)>>>();
        foreach (Match ev in Regex.Matches(raw, @"BEGIN:VEVENT(.*?)END:VEVENT", RegexOptions.Singleline)) evs.Add(Props(ev.Groups[1].Value));

        // изменённые вхождения повторяющихся событий: оригинальное вхождение пропускаем
        var overridden = new HashSet<string>();
        foreach (var p in evs)
            if (p.TryGetValue("RECURRENCE-ID", out var rid) && p.TryGetValue("UID", out var uid)
                && TryParseIcsDate(rid[0].val, rid[0].par, out var rdt, out _))
                overridden.Add(uid[0].val + "|" + rdt.ToString("yyyyMMddHHmm"));

        foreach (var p in evs)
        {
            if (p.TryGetValue("STATUS", out var st) && st[0].val.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            if (!p.TryGetValue("DTSTART", out var ds)) continue;
            if (!TryParseIcsDate(ds[0].val, ds[0].par, out var start, out bool allDay)) continue;
            string title = p.TryGetValue("SUMMARY", out var sm) ? Unesc(sm[0].val) : "";
            if (title.Length == 0) title = "Событие";

            DateTime? end = null;
            if (p.TryGetValue("DTEND", out var de) && TryParseIcsDate(de[0].val, de[0].par, out var e2, out _)) end = e2;
            var dur = end.HasValue ? end.Value - start : (allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;

            if (p.TryGetValue("RRULE", out var rr))
            {
                var ex = new HashSet<string>();
                if (p.TryGetValue("EXDATE", out var exl))
                    foreach (var x in exl)
                        foreach (var part in x.val.Split(','))
                            if (TryParseIcsDate(part.Trim(), x.par, out var xd, out _)) ex.Add(xd.ToString("yyyyMMddHHmm"));
                string uid = p.TryGetValue("UID", out var u) ? u[0].val : "";
                foreach (var occ in Expand(start, rr[0].val, to))
                {
                    string key = occ.ToString("yyyyMMddHHmm");
                    if (ex.Contains(key) || overridden.Contains(uid + "|" + key)) continue;
                    if (occ + dur < from) continue;
                    res.Add(new CalEvent(occ, title, allDay, dur > TimeSpan.Zero ? occ + dur : null));
                }
            }
            else if (start <= to && start + dur >= from)
                res.Add(new CalEvent(start, title, allDay, dur > TimeSpan.Zero ? start + dur : null));
            if (res.Count > 6000) break;
        }
        return res.OrderBy(e => e.Start).ToList();
    }

    static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime to)
    {
        var p = new Dictionary<string, string>();
        foreach (var kv in rule.Split(';')) { var a = kv.Split('='); if (a.Length == 2) p[a[0].ToUpperInvariant()] = a[1]; }
        string freq = p.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        int interval = p.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var ivn) && ivn > 0 ? ivn : 1;
        int count = p.TryGetValue("COUNT", out var cv) && int.TryParse(cv, out var cn) ? cn : int.MaxValue;
        DateTime until = to;
        if (p.TryGetValue("UNTIL", out var uv) && TryParseIcsDate(uv, "", out var ud, out bool udAll))
        {
            if (udAll) ud = ud.AddDays(1).AddTicks(-1);
            if (ud < until) until = ud;
        }
        var tod = start.TimeOfDay;
        int n = 0, guard = 0;

        static int Dow(string s) => s.ToUpperInvariant() switch { "MO" => 1, "TU" => 2, "WE" => 3, "TH" => 4, "FR" => 5, "SA" => 6, _ => 0 };

        if (freq == "DAILY")
        {
            for (int i = 0; guard++ < 5000; i++)
            {
                var d = start.AddDays((double)i * interval);
                if (d > until || n >= count) yield break;
                n++; yield return d;
            }
        }
        else if (freq == "WEEKLY")
        {
            var days = new List<int>();
            if (p.TryGetValue("BYDAY", out var bd))
                foreach (var x in bd.Split(',')) { var t = x.Trim(); if (t.Length >= 2) days.Add(Dow(t[^2..])); }
            if (days.Count == 0) days.Add((int)start.DayOfWeek);
            var offs = days.Select(dw => (dw + 6) % 7).Distinct().OrderBy(x => x).ToList();
            var ws = start.Date.AddDays(-((int)start.DayOfWeek + 6) % 7);
            for (int w = 0; guard++ < 2000; w++)
                foreach (var o in offs)
                {
                    var d = ws.AddDays((double)w * 7 * interval + o) + tod;
                    if (d < start) continue;
                    if (d > until || n >= count) yield break;
                    n++; yield return d;
                }
        }
        else if (freq == "MONTHLY")
        {
            string byday = p.GetValueOrDefault("BYDAY", "");
            int md = p.TryGetValue("BYMONTHDAY", out var bmd) && int.TryParse(bmd.Split(',')[0], out var bmn) ? bmn : start.Day;
            for (int m = 0; guard++ < 2000; m++)
            {
                var first = new DateTime(start.Year, start.Month, 1).AddMonths(m * interval);
                DateTime? day = null;
                if (byday.Length >= 3 && int.TryParse(byday[..^2], out int nth)) day = NthWeekday(first.Year, first.Month, Dow(byday[^2..]), nth);
                else if (md > 0 && md <= DateTime.DaysInMonth(first.Year, first.Month)) day = new DateTime(first.Year, first.Month, md);
                else if (md < 0 && -md <= DateTime.DaysInMonth(first.Year, first.Month)) day = new DateTime(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month) + md + 1);
                if (day == null) { if (first > until) yield break; continue; }
                var d = day.Value + tod;
                if (d < start) continue;
                if (d > until || n >= count) yield break;
                n++; yield return d;
            }
        }
        else if (freq == "YEARLY")
        {
            for (int y = 0; guard++ < 200; y++)
            {
                int yr = start.Year + y * interval;
                if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(yr)) { if (new DateTime(Math.Min(yr, 9998), 1, 1) > until) yield break; continue; }
                if (yr > 9998) yield break;
                var d = new DateTime(yr, start.Month, start.Day) + tod;
                if (d > until || n >= count) yield break;
                n++; yield return d;
            }
        }
        else yield return start;
    }

    static DateTime? NthWeekday(int year, int month, int dow, int nth)
    {
        int dim = DateTime.DaysInMonth(year, month);
        if (nth > 0)
        {
            var f = new DateTime(year, month, 1);
            int delta = (dow - (int)f.DayOfWeek + 7) % 7;
            int day = 1 + delta + 7 * (nth - 1);
            return day <= dim ? new DateTime(year, month, day) : null;
        }
        if (nth < 0)
        {
            var l = new DateTime(year, month, dim);
            int delta = ((int)l.DayOfWeek - dow + 7) % 7;
            int day = dim - delta + 7 * (nth + 1);
            return day >= 1 ? new DateTime(year, month, day) : null;
        }
        return null;
    }

    static bool TryParseIcsDate(string s, string par, out DateTime dt) => TryParseIcsDate(s, par, out dt, out _);

    static bool TryParseIcsDate(string s, string par, out DateTime dt, out bool allDay)
    {
        dt = default; s = s.Trim();
        allDay = s.Length == 8;
        string[] fmts = { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
        bool utc = s.EndsWith("Z");
        if (!DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return false;
        if (utc) { dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(); return true; }
        var tz = Regex.Match(par ?? "", @"TZID=""?([^"";:]+)");
        if (tz.Success && !allDay)
        {
            try { dt = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(tz.Groups[1].Value), TimeZoneInfo.Local); }
            catch { /* неизвестный пояс — считаем местным временем */ }
        }
        return true;
    }
}
