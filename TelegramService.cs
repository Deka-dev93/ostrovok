using System.Text.RegularExpressions;

namespace Ostrovok.Services;

/// <summary>Состояние Telegram: что пришло (из фонового окна Telegram Web) и сколько непрочитанных.</summary>
public class TelegramService
{
    public List<IslandNotice> Recent { get; } = new();
    public int Unread { get; private set; }
    public event Action Changed;

    public void OnNotice(string title, string body)
    {
        title = (title ?? "").Trim(); body = (body ?? "").Trim();
        Recent.Insert(0, new IslandNotice("Telegram", title, body, DateTime.Now));
        if (Recent.Count > 30) Recent.RemoveAt(Recent.Count - 1);
        Changed?.Invoke();
        Hub.Notices.Push("Telegram", "✈ " + title, body);
    }

    /// <summary>Telegram Web пишет число непрочитанных в заголовок вкладки: «(3) Telegram Web».</summary>
    public void ParseTitle(string title)
    {
        int n = 0;
        var m = Regex.Match(title ?? "", @"^\((\d+)\)");
        if (m.Success) int.TryParse(m.Groups[1].Value, out n);
        if (n == Unread) return;
        Unread = n; Changed?.Invoke();
    }
}
