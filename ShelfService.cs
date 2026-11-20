using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ostrovok.Services;

/// <summary>
/// Полка: папка с файлами + маленький сервер для телефона в домашней сети.
/// Доступ только по длинной случайной ссылке; сервер запускается лишь если включён в «Полке».
/// </summary>
public class ShelfService
{
    public static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ostrovok", "Shelf");
            Directory.CreateDirectory(d); return d;
        }
    }

    public event Action Changed;
    public string Url { get; private set; } = "";
    public string Error { get; private set; } = "";
    public bool Running => _app != null;
    WebApplication _app;

    public List<string> Files() =>
        Directory.EnumerateFiles(Dir).OrderByDescending(File.GetLastWriteTime).ToList();

    public string Add(string src)
    {
        try
        {
            var name = Path.GetFileName(src);
            var dst = Unique(name);
            File.Copy(src, dst);
            Changed?.Invoke();
            return dst;
        }
        catch (Exception ex) { AppSettings.Log("shelf add: " + ex.Message); return null; }
    }

    public void Remove(string path) { try { File.Delete(path); } catch { } Changed?.Invoke(); }
    public void Clear() { foreach (var f in Files()) { try { File.Delete(f); } catch { } } Changed?.Invoke(); }

    static string Unique(string name)
    {
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var p = Path.Combine(Dir, name);
        int i = 1;
        while (File.Exists(p))
            p = Path.Combine(Dir, Path.GetFileNameWithoutExtension(name) + $" ({i++})" + Path.GetExtension(name));
        return p;
    }

    /// <summary>Другие адреса этого ПК в локальных сетях (если QR ведёт не туда — их можно ввести на телефоне вручную).</summary>
    public List<string> OtherIps { get; private set; } = new();

    static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    /// <summary>
    /// Выбирает адрес ПК в домашней сети. Раньше брался первый адаптер со шлюзом — часто это был VPN,
    /// виртуальный адаптер (Hyper-V, WSL, VirtualBox) и т.п., и телефон по такой ссылке не открывал ничего.
    /// </summary>
    List<string> LocalIps()
    {
        var found = new List<(string ip, int score)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var label = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool virt = new[] { "vethernet", "virtual", "vmware", "vbox", "hyper-v", "wsl", "tap-", "tap ", "wintun", "wireguard", "vpn", "tailscale", "zerotier", "bluetooth", "npcap", "loopback", "docker", "radmin", "hamachi" }
                            .Any(label.Contains);
                var props = ni.GetIPProperties();
                bool gw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var s = u.Address.ToString();
                    if (s.StartsWith("169.254.") || s.StartsWith("127.")) continue;      // APIPA / loopback — телефон не достанет
                    int score = 0;
                    if (IsPrivate(u.Address)) score += 40;
                    if (s.StartsWith("192.168.")) score += 10;
                    if (gw) score += 30;
                    if (!virt) score += 50;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 8;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 5;
                    found.Add((s, score));
                }
            }
        }
        catch { }
        return found.OrderByDescending(x => x.score).Select(x => x.ip).Distinct().ToList();
    }

    /// <summary>
    /// Открывает входящий порт в брандмауэре Windows для этой программы. Без этого телефон «не видит» ПК:
    /// Windows молча блокирует подключения, особенно если окно с вопросом закрыли или сеть названа «Общедоступной».
    /// Нужны права администратора — Windows один раз покажет запрос (UAC), нужно нажать «Да».
    /// </summary>
    public bool FirewallAsked { get; private set; }
    public string FirewallNote { get; private set; } = "";

    async Task EnsureFirewallAsync()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || string.Equals(Hub.S.FirewallPath, exe, StringComparison.OrdinalIgnoreCase)) return;
        FirewallNote = "";
        try
        {
            FirewallAsked = true;
            // сначала удаляем все старые правила для этой программы (в том числе автоматические «Блокировать» от закрытого окна Windows),
            // потом добавляем разрешающее правило для любых сетей
            var args = "/c netsh advfirewall firewall delete rule name=all dir=in program=\"" + exe + "\" & " +
                       "netsh advfirewall firewall add rule name=\"Ostrovok Shelf\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=any protocol=TCP";
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", args) { Verb = "runas", UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            int code = await Task.Run(() =>
            {
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return -1;
                p.WaitForExit(30000);
                return p.HasExited ? p.ExitCode : -2;
            });
            if (code == 0) { Hub.S.FirewallPath = exe; Hub.S.Save(); }
            else FirewallNote = "Не удалось открыть доступ в брандмауэре Windows (код " + code + ").";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            FirewallNote = "Вы отклонили запрос Windows — телефон не сможет подключиться. Выключите и снова включите полку и нажмите «Да».";
        }
        catch (Exception ex) { FirewallNote = "Брандмауэр: " + ex.Message; AppSettings.Log("shelf firewall: " + ex); }
    }

    public async Task StartAsync()
    {
        if (_app != null) return;
        Error = "";
        var s = Hub.S;
        if (string.IsNullOrEmpty(s.ShelfToken)) s.ShelfToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        if (s.ShelfPort == 0) s.ShelfPort = Random.Shared.Next(30000, 60000);
        try
        {
            WebApplication app = null;
            for (int attempt = 0; attempt < 4 && app == null; attempt++)
            {
                try
                {
                    var b = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
                    b.Logging.ClearProviders();
                    b.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Any, s.ShelfPort); o.Limits.MaxRequestBodySize = null; });
                    b.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = long.MaxValue; o.ValueLengthLimit = int.MaxValue; });
                    var a = b.Build();
                    Map(a, s.ShelfToken);
                    await a.StartAsync();
                    app = a;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    AppSettings.Log("shelf port busy: " + ex.Message);
                    s.ShelfPort = Random.Shared.Next(30000, 60000);
                }
            }
            _app = app;
            var ips = LocalIps();
            if (ips.Count == 0) ips.Add("127.0.0.1");
            OtherIps = ips.Skip(1).Select(i => $"http://{i}:{s.ShelfPort}/{s.ShelfToken}/").ToList();
            Url = $"http://{ips[0]}:{s.ShelfPort}/{s.ShelfToken}/";
            s.Save();
            Changed?.Invoke();                    // QR уже можно показать
            await EnsureFirewallAsync();          // а теперь — один раз просим Windows открыть доступ телефону
        }
        catch (Exception ex) { Error = ex.Message; AppSettings.Log("shelf start: " + ex); _app = null; }
        Changed?.Invoke();
    }

    public async Task StopAsync()
    {
        var a = _app; _app = null; Url = "";
        try { if (a != null) await a.StopAsync(); } catch { }
        Changed?.Invoke();
    }

    void Map(WebApplication a, string token)
    {
        a.MapGet("/{t}/", (string t) => t != token ? Results.NotFound() : Results.Content(Page(t), "text/html; charset=utf-8"));

        a.MapGet("/{t}/files/{name}", (string t, string name) =>
        {
            if (t != token) return Results.NotFound();
            var p = Path.Combine(Dir, Path.GetFileName(name));
            return File.Exists(p) ? Results.File(p, "application/octet-stream", Path.GetFileName(p), enableRangeProcessing: true) : Results.NotFound();
        });

        a.MapPost("/{t}/upload", async (string t, HttpRequest r) =>
        {
            if (t != token) return Results.NotFound();
            var form = await r.ReadFormAsync();
            int n = 0; string last = "";
            foreach (var f in form.Files)
            {
                var dst = Unique(Path.GetFileName(f.FileName));
                await using (var fs = File.Create(dst)) await f.CopyToAsync(fs);
                n++; last = Path.GetFileName(dst);
            }
            if (n > 0)
            {
                Changed?.Invoke();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    Hub.Notices.Push("Полка", "📱 С телефона", n == 1 ? last : $"{n} файлов"));
            }
            return Results.Redirect($"/{t}/?ok=1");
        });

        a.MapPost("/{t}/text", async (string t, HttpRequest r) =>
        {
            if (t != token) return Results.NotFound();
            var form = await r.ReadFormAsync();
            string text = form["text"].ToString();
            if (!string.IsNullOrWhiteSpace(text))
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try { System.Windows.Clipboard.SetText(text); } catch { }
                    Hub.Notices.Push("Полка", "📱 Текст с телефона", "Скопирован в буфер обмена");
                });
            return Results.Redirect($"/{t}/?ok=1");
        });
    }

    string Page(string t)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Островок</title><style>
body{font-family:-apple-system,Segoe UI,sans-serif;background:#000;color:#f2f2f2;margin:0;padding:0 16px 24px;max-width:560px;margin:auto}
.hd{margin:0 -16px;padding:34px 22px 30px;background:linear-gradient(#a3a4ff,#7f80f2);border-radius:0 0 34px 34px;color:#10102a}
.hd p{margin:4px 0 0;opacity:.7;font-size:14px}
h1{font-size:26px;margin:0}h2{font-size:15px;color:#aaa;margin-top:26px}
.card{background:#121214;border-radius:16px;padding:14px;margin:10px 0}
input[type=file]{width:100%;color:#ccc}textarea{width:100%;box-sizing:border-box;background:#2a2a2c;color:#fff;border:0;border-radius:10px;padding:10px;font-size:16px;min-height:80px}
button{background:#8b8cff;color:#0b0b1f;font-weight:600;border:0;border-radius:12px;padding:11px 18px;font-size:16px;margin-top:10px}
a{color:#a3a4ff;text-decoration:none;display:block;padding:8px 0;border-bottom:1px solid #2a2a2c;word-break:break-all}
.ok{background:#1a1a3a;color:#c9caff;padding:10px 14px;border-radius:12px}
</style></head><body><div class=hd><h1>На компьютер</h1><p>Подключено к острову по домашнему Wi-Fi</p></div>
""");
        sb.Append("<div id=ok></div><script>if(location.search.includes('ok=1'))document.getElementById('ok').innerHTML='<div class=ok>Готово — отправлено на компьютер</div>'</script>");
        sb.Append($"<h2>Отправить файлы на компьютер</h2><form class=card method=post enctype=multipart/form-data action=\"/{t}/upload\"><input type=file name=files multiple><button>Отправить</button></form>");
        sb.Append($"<h2>Отправить текст в буфер обмена ПК</h2><form class=card method=post action=\"/{t}/text\"><textarea name=text placeholder=\"Текст или ссылка\"></textarea><button>Отправить</button></form>");
        sb.Append("<h2>Полка на компьютере</h2><div class=card>");
        var files = Files();
        if (files.Count == 0) sb.Append("Пока пусто");
        foreach (var f in files)
        {
            var n = Path.GetFileName(f);
            sb.Append($"<a href=\"/{t}/files/{Uri.EscapeDataString(n)}\">⬇ {WebUtility.HtmlEncode(n)}</a>");
        }
        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
