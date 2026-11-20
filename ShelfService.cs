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

    static string LocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var ip = ni.GetIPProperties();
                if (!ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                var u = ip.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (u != null) return u.Address.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
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
            Url = $"http://{LocalIp()}:{s.ShelfPort}/{s.ShelfToken}/";
            s.Save();
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
body{font-family:-apple-system,Segoe UI,sans-serif;background:#111;color:#f2f2f2;margin:0;padding:18px;max-width:560px;margin:auto}
h1{font-size:22px}h2{font-size:15px;color:#aaa;margin-top:26px}
.card{background:#1d1d1f;border-radius:16px;padding:14px;margin:10px 0}
input[type=file]{width:100%;color:#ccc}textarea{width:100%;box-sizing:border-box;background:#2a2a2c;color:#fff;border:0;border-radius:10px;padding:10px;font-size:16px;min-height:80px}
button{background:#0a84ff;color:#fff;border:0;border-radius:12px;padding:11px 18px;font-size:16px;margin-top:10px}
a{color:#4da3ff;text-decoration:none;display:block;padding:8px 0;border-bottom:1px solid #2a2a2c;word-break:break-all}
.ok{background:#1f3d27;color:#9fe3b0;padding:10px 14px;border-radius:12px}
</style></head><body><h1>🏝 Островок</h1>
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
