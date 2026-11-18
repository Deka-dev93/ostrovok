using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Ostrovok.Services;

public class MailAccount
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
}

public record MailItem(string From, string Subject, DateTime Date, uint Uid);
public record MailPreset(string Name, string Host, string Web, string Help, string HelpUrl);

public class MailState
{
    public MailAccount Acc;
    public List<MailItem> Items = new();
    public int Unread;
    public string Error = "";
    public bool Baseline;
    public HashSet<uint> Seen = new();
    public DateTime Checked;
}

/// <summary>
/// Почта по IMAP: Gmail, Mail.ru, Яндекс и др. Пароли хранятся только на этом компьютере,
/// зашифрованные средствами Windows (DPAPI), письма не скачиваются — только отправитель и тема.
/// </summary>
public class MailboxService
{
    public List<MailState> States { get; } = new();
    public event Action Changed;
    public int TotalUnread => States.Sum(s => s.Unread);
    public bool Busy { get; private set; }

    static string FilePath => Path.Combine(AppSettings.DataDir, "mail.dat");
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(2) };

    public static MailPreset PresetFor(string email)
    {
        string d = (email.Contains('@') ? email[(email.IndexOf('@') + 1)..] : "").Trim().ToLowerInvariant();
        switch (d)
        {
            case "gmail.com": case "googlemail.com":
                return new("Gmail", "imap.gmail.com", "https://mail.google.com",
                    "Gmail не принимает обычный пароль. Включите двухэтапную защиту и создайте «Пароль приложения» (16 букв), вставьте его сюда.",
                    "https://myaccount.google.com/apppasswords");
            case "mail.ru": case "inbox.ru": case "list.ru": case "bk.ru": case "internet.ru":
                return new("Mail.ru", "imap.mail.ru", "https://e.mail.ru/inbox",
                    "Mail.ru: Настройки → Пароли для внешних приложений → создать пароль (доступ «Почтовые программы») и вставить его сюда.",
                    "https://account.mail.ru/user/2-step-auth/passwords");
            case "yandex.ru": case "ya.ru": case "yandex.com": case "yandex.by": case "yandex.kz": case "yandex.ua":
                return new("Яндекс", "imap.yandex.ru", "https://mail.yandex.ru",
                    "Яндекс: в настройках почты включите доступ по IMAP, затем в id.yandex.ru → Безопасность создайте «Пароль приложения» для почты.",
                    "https://id.yandex.ru/security/app-passwords");
            case "rambler.ru": case "lenta.ru": case "autorambler.ru": case "myrambler.ru":
                return new("Rambler", "imap.rambler.ru", "https://mail.rambler.ru", "Rambler: включите доступ по IMAP в настройках почты и введите обычный пароль.", "");
            case "icloud.com": case "me.com": case "mac.com":
                return new("iCloud", "imap.mail.me.com", "https://www.icloud.com/mail",
                    "iCloud: создайте пароль для приложения на appleid.apple.com и вставьте его сюда.", "https://appleid.apple.com");
            case "yahoo.com": case "ymail.com":
                return new("Yahoo", "imap.mail.yahoo.com", "https://mail.yahoo.com",
                    "Yahoo: создайте пароль приложения в настройках безопасности аккаунта.", "https://login.yahoo.com/account/security");
            case "outlook.com": case "hotmail.com": case "live.com": case "msn.com":
                return new("Outlook", "outlook.office365.com", "https://outlook.live.com",
                    "Outlook/Hotmail: Microsoft отключил вход по паролю для сторонних программ, поэтому подключить такой ящик не получится.", "");
            case "":
                return new("", "", "", "Введите адрес почты — подскажу, где взять пароль для программ.", "");
            default:
                return new("IMAP", "imap." + d, "https://" + d, "Для " + d + " используется сервер imap." + d + ". Если не подключается — включите IMAP в настройках почты и создайте пароль для приложений.", "");
        }
    }

    public void Start()
    {
        Load();
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<MailAccount>>(Encoding.UTF8.GetString(plain)) ?? new();
            foreach (var a in list) States.Add(new MailState { Acc = a });
        }
        catch (Exception ex) { AppSettings.Log("mail load: " + ex.Message); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            var json = JsonSerializer.Serialize(States.Select(s => s.Acc).ToList());
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) { AppSettings.Log("mail save: " + ex.Message); }
    }

    /// <summary>Добавляет ящик. Возвращает null при успехе или текст ошибки.</summary>
    public async Task<string> AddAsync(string email, string password)
    {
        email = email.Trim(); password = password.Trim().Replace(" ", "");
        if (!email.Contains('@') || password.Length == 0) return "Введите адрес и пароль.";
        if (States.Any(s => s.Acc.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) return "Этот ящик уже добавлен.";
        var p = PresetFor(email);
        if (p.Name == "Outlook") return p.Help;
        var acc = new MailAccount { Email = email, Password = password, Host = p.Host, Port = 993 };
        try
        {
            var (unread, items) = await Task.Run(() => Fetch(acc));
            var st = new MailState { Acc = acc, Unread = unread, Items = items, Checked = DateTime.Now, Baseline = true };
            foreach (var i in items) st.Seen.Add(i.Uid);
            States.Add(st);
            Save();
            Changed?.Invoke();
            return null;
        }
        catch (Exception ex) { return Explain(ex); }
    }

    public void Remove(MailState s)
    {
        States.Remove(s);
        Save();
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        if (Busy || States.Count == 0) return;
        Busy = true; Changed?.Invoke();
        try
        {
            foreach (var st in States.ToList())
            {
                try
                {
                    var (unread, items) = await Task.Run(() => Fetch(st.Acc));
                    st.Error = "";
                    var fresh = items.Where(i => !st.Seen.Contains(i.Uid)).ToList();
                    foreach (var i in items) st.Seen.Add(i.Uid);
                    st.Items = items; st.Unread = unread; st.Checked = DateTime.Now;
                    if (st.Baseline)
                    {
                        // новые письма — сообщаем (не больше трёх за раз, чтобы не заспамить остров)
                        foreach (var i in fresh.OrderBy(x => x.Date).TakeLast(3))
                            Hub.Notices.Push("Почта", "✉ " + i.From, i.Subject);
                    }
                    st.Baseline = true;     // первый опрос после запуска — тихий
                }
                catch (Exception ex) { st.Error = Explain(ex); AppSettings.Log("mail " + st.Acc.Email + ": " + ex.Message); }
            }
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    static string Explain(Exception ex)
    {
        if (ex is AuthenticationException) return "Не удалось войти: проверьте адрес и пароль для приложений, а также включён ли IMAP.";
        if (ex is TimeoutException || ex is System.Net.Sockets.SocketException || ex is IOException) return "Нет связи с почтовым сервером.";
        return ex.Message.Length > 140 ? ex.Message[..140] : ex.Message;
    }

    static (int unread, List<MailItem> items) Fetch(MailAccount a)
    {
        using var c = new ImapClient { Timeout = 20000 };
        c.Connect(a.Host, a.Port, SecureSocketOptions.SslOnConnect);
        c.Authenticate(a.Email, a.Password);
        var inbox = c.Inbox;
        inbox.Open(MailKit.FolderAccess.ReadOnly);
        var uids = inbox.Search(SearchQuery.NotSeen);
        var take = uids.Skip(Math.Max(0, uids.Count - 20)).ToList();
        var items = new List<MailItem>();
        if (take.Count > 0)
        {
            var sum = inbox.Fetch(take, MailKit.MessageSummaryItems.Envelope | MailKit.MessageSummaryItems.InternalDate | MailKit.MessageSummaryItems.UniqueId);
            foreach (var m in sum)
            {
                var from = m.Envelope?.From?.Mailboxes?.FirstOrDefault();
                string who = from == null ? "?" : (string.IsNullOrWhiteSpace(from.Name) ? from.Address : from.Name);
                string subj = string.IsNullOrWhiteSpace(m.Envelope?.Subject) ? "(без темы)" : m.Envelope.Subject;
                var when = m.InternalDate?.LocalDateTime ?? DateTime.Now;
                items.Add(new MailItem(who, subj, when, m.UniqueId.Id));
            }
        }
        c.Disconnect(true);
        return (uids.Count, items.OrderByDescending(i => i.Date).ToList());
    }
}
