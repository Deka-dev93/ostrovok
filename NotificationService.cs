using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Ostrovok.Services;

public record IslandNotice(string App, string Title, string Body, DateTime Time);

/// <summary>
/// Уведомления на острове. Свои (таймеры, напоминания) работают всегда.
/// Уведомления Windows (Telegram, Discord…) читаются через UserNotificationListener —
/// Windows разрешает это не всем неупакованным приложениям, тогда этот канал просто молчит.
/// </summary>
public class NotificationService
{
    public List<IslandNotice> History { get; } = new();
    public int Unread { get; private set; }
    public event Action<IslandNotice> Arrived;
    public bool SystemAccess { get; private set; }

    readonly HashSet<uint> _seen = new();
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    UserNotificationListener _l;

    public void Push(string app, string title, string body)
    {
        var n = new IslandNotice(app, title, body, DateTime.Now);
        History.Insert(0, n);
        if (History.Count > 50) History.RemoveAt(History.Count - 1);
        Unread++;
        Arrived?.Invoke(n);
    }

    public void MarkRead() => Unread = 0;

    public async Task InitSystemAsync()
    {
        try
        {
            _l = UserNotificationListener.Current;
            var access = await _l.RequestAccessAsync();
            SystemAccess = access == UserNotificationListenerAccessStatus.Allowed;
            if (!SystemAccess) { AppSettings.Log("notifications: access " + access); return; }

            foreach (var n in await _l.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id); // старые не показываем
            _poll.Tick += async (_, _) => await PollAsync();
            _poll.Start();
        }
        catch (Exception ex) { AppSettings.Log("notifications init: " + ex.Message); }
    }

    async Task PollAsync()
    {
        try
        {
            var list = await _l.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in list)
            {
                if (!_seen.Add(n.Id)) continue;
                var binding = n.Notification.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding == null) continue;
                var texts = binding.GetTextElements().Select(t => t.Text).ToList();
                string title = texts.Count > 0 ? texts[0] : "";
                string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : "";
                string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                Push(app, title, body);
            }
        }
        catch (Exception ex) { AppSettings.Log("notifications poll: " + ex.Message); }
    }
}
