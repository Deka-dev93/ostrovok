using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;

namespace Ostrovok.Services;

public class HeadphoneInfo
{
    public string Id = "", Name = "";
    public int Battery = -1;      // -1 — устройство не сообщает заряд
    public int Tries;
}

/// <summary>
/// Подключённые Bluetooth-наушники и их заряд. Заряд виден, только если наушники сообщают его Windows
/// (так делают большинство современных моделей, включая AirPods).
/// </summary>
public class HeadphoneService
{
    public List<HeadphoneInfo> Devices { get; } = new();
    public event Action Changed;

    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    bool _busy, _baseline;
    DateTime _lastBat = DateTime.MinValue;

    public void Start()
    {
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    async Task PollAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var sel = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var infos = await DeviceInformation.FindAllAsync(sel);
            var now = new List<HeadphoneInfo>();
            foreach (var di in infos)
            {
                using var bd = await BluetoothDevice.FromIdAsync(di.Id);
                if (bd == null || bd.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo) continue;
                var old = Devices.FirstOrDefault(d => d.Id == di.Id);
                now.Add(old ?? new HeadphoneInfo { Id = di.Id, Name = string.IsNullOrWhiteSpace(bd.Name) ? di.Name : bd.Name });
            }

            bool needBat = now.Count > 0 &&
                (now.Any(d => d.Battery < 0 && d.Tries < 6) && (DateTime.Now - _lastBat).TotalSeconds > 10
                 || (DateTime.Now - _lastBat).TotalSeconds > 60);
            Dictionary<string, int> bats = null;
            if (needBat) { bats = await ReadBatteriesAsync(); _lastBat = DateTime.Now; }

            bool changed = false;
            var announce = new List<(string title, string body)>();

            foreach (var d in now)
            {
                bool isNew = !Devices.Contains(d);
                int prev = d.Battery;
                if (bats != null)
                {
                    d.Tries++;
                    int b = Match(bats, d.Name);
                    if (b >= 0) d.Battery = b;
                }
                if (isNew)
                {
                    changed = true;
                    if (_baseline) announce.Add(("🎧 " + d.Name, d.Battery >= 0 ? $"подключены · заряд {d.Battery}%" : "подключены"));
                }
                else if (prev < 0 && d.Battery >= 0) { changed = true; announce.Add(("🎧 " + d.Name, $"заряд {d.Battery}%")); }
                else if (prev != d.Battery) changed = true;
                if (prev > 20 && d.Battery >= 0 && d.Battery <= 20) announce.Add(("🎧 " + d.Name, $"низкий заряд — {d.Battery}%"));
            }
            foreach (var gone in Devices.Where(d => !now.Contains(d)).ToList())
            {
                changed = true;
                if (_baseline) announce.Add(("🎧 " + gone.Name, "отключены"));
            }

            Devices.Clear(); Devices.AddRange(now);
            _baseline = true;
            foreach (var (t, b) in announce) Hub.Notices.Push("Наушники", t, b);
            if (changed) Changed?.Invoke();
        }
        catch (Exception ex) { AppSettings.Log("headphones: " + ex.Message); }
        finally { _busy = false; }
    }

    static int Match(Dictionary<string, int> bats, string name)
    {
        if (bats.TryGetValue(name, out var v)) return v;
        foreach (var kv in bats)
            if (kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return -1;
    }

    static async Task<Dictionary<string, int>> ReadBatteriesAsync()
    {
        var res = new Dictionary<string, int>();
        try
        {
            var all = await PnpObject.FindAllAsync(PnpObjectType.Device, new[] { BatteryKey, "System.ItemNameDisplay" });
            foreach (var p in all)
            {
                if (!p.Properties.TryGetValue(BatteryKey, out var v) || v == null) continue;
                string name = p.Properties.TryGetValue("System.ItemNameDisplay", out var n) && n != null ? n.ToString() : p.Id;
                try { int b = Convert.ToInt32(v); if (b >= 0 && b <= 100) res[name] = b; } catch { }
            }
        }
        catch (Exception ex) { AppSettings.Log("headphones battery: " + ex.Message); }
        return res;
    }
}
