using System.IO;
using System.Windows.Threading;

namespace Ostrovok.Services;

/// <summary>Следит за папкой «Загрузки»: пока браузер качает файл, остров показывает имя и скорость.</summary>
public class DownloadService
{
    static readonly string[] Exts = { ".crdownload", ".part", ".download", ".opdownload" };
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<string, (long size, DateTime at)> _seen = new();
    public bool Active { get; private set; }
    public string Name { get; private set; } = "";
    public double Speed { get; private set; }   // байт/с
    public event Action Changed;

    public string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public string SpeedText => Speed >= 1048576 ? $"{Speed / 1048576:0.#} МБ/с" : Speed >= 1024 ? $"{Speed / 1024:0} КБ/с" : "…";

    public void Start()
    {
        _t.Tick += (_, _) => Poll();
        _t.Start();
    }

    void Poll()
    {
        try
        {
            if (!Hub.S.ShowDownloads || !Directory.Exists(Dir)) { Set(false, "", 0); return; }
            var now = DateTime.Now;
            var cur = Directory.EnumerateFiles(Dir).Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            double best = -1; string bestName = ""; bool any = false;
            foreach (var f in cur)
            {
                long size = new FileInfo(f).Length;
                double sp = 0;
                if (_seen.TryGetValue(f, out var prev))
                {
                    double dt = Math.Max(0.2, (now - prev.at).TotalSeconds);
                    sp = Math.Max(0, (size - prev.size) / dt);
                    if (size > prev.size) any = true;
                }
                else any = true;
                _seen[f] = (size, now);
                if (sp > best) { best = sp; bestName = Path.GetFileNameWithoutExtension(f); }
            }
            foreach (var gone in _seen.Keys.Where(k => !cur.Contains(k)).ToList())
            {
                _seen.Remove(gone);
                var final = Path.Combine(Dir, Path.GetFileNameWithoutExtension(gone));
                if (File.Exists(final)) Hub.Notices.Push("Загрузки", "✅ " + Path.GetFileName(final), "Скачано");
            }
            if (cur.Count > 0 && any) Set(true, bestName, Math.Max(0, best));
            else Set(false, "", 0);
        }
        catch { }
    }

    void Set(bool active, string name, double speed)
    {
        bool ch = active != Active || (active && (name != Name || Math.Abs(speed - Speed) > 1));
        Active = active; Name = name; Speed = speed;
        if (ch) Changed?.Invoke();
    }
}
