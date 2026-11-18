using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok.Views;

static class Links
{
    public static void Open(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
    static ControlTemplate _flat;
    /// <summary>Невидимая кнопка-обёртка: клик по карточке работает, а остров не принимает его за перетаскивание.</summary>
    public static Button Clickable(UIElement content, Action click)
    {
        if (_flat == null)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var f = new FrameworkElementFactory(typeof(ContentPresenter));
            f.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            f.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = f;
            _flat = tpl;
        }
        var b = new Button { Template = _flat, Content = content, Cursor = Cursors.Hand, Padding = new Thickness(0),
                             Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => click();
        return b;
    }
    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "сейчас";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} мин";
        if (d.TotalHours < 24) return t.ToString("HH:mm");
        return t.ToString("dd.MM HH:mm");
    }
}

// ═════════════ 📧 Почта ═════════════
public class MailTab : TabPage
{
    readonly StackPanel _list = new();

    public MailTab()
    {
        var dock = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(btns, Dock.Right);
        btns.Children.Add(UI.Btn("＋ Ящик", () => MailAddWindow.ShowIt()));
        btns.Children.Add(UI.Btn("⟳", () => _ = Hub.Mail.RefreshAsync()));
        top.Children.Add(btns);
        top.Children.Add(UI.Text("📧 Почта", 15, true));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Children.Add(dock);
        Hub.Mail.Changed += () => Dispatcher.BeginInvoke(Rebuild);
        Rebuild();
    }

    public override void OnShown() => Rebuild();

    void Rebuild()
    {
        _list.Children.Clear();
        if (Hub.Mail.States.Count == 0)
        {
            var p = new StackPanel { Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(UI.Text("Подключите почту — новые письма появятся здесь и на острове", 13, false, true));
            var b = UI.Btn("＋ Добавить ящик", () => MailAddWindow.ShowIt(), true);
            b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(b);
            p.Children.Add(UI.Text("Gmail, Mail.ru, Яндекс, Rambler, iCloud, Yahoo", 11, false, true));
            _list.Children.Add(p);
            return;
        }

        foreach (var st in Hub.Mail.States.ToList())
        {
            var head = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(right, Dock.Right);
            var s2 = st;
            var web = MailboxService.PresetFor(st.Acc.Email).Web;
            right.Children.Add(UI.Btn("Открыть", () => Links.Open(web)));
            right.Children.Add(UI.Btn("✕", () =>
            {
                if (MessageBox.Show("Отключить " + s2.Acc.Email + "?", "Островок", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Hub.Mail.Remove(s2);
            }));
            head.Children.Add(right);
            head.Children.Add(UI.Text($"{st.Acc.Email}  ·  {(st.Unread > 0 ? st.Unread + " новых" : "нет новых")}", 12.5, true));
            _list.Children.Add(head);

            if (!string.IsNullOrEmpty(st.Error))
            {
                var e = UI.Text("⚠ " + st.Error, 11.5); e.TextWrapping = TextWrapping.Wrap; e.TextTrimming = TextTrimming.None;
                e.Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120));
                _list.Children.Add(e);
            }
            foreach (var m in st.Items.Take(8))
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var col = new StackPanel();
                col.Children.Add(UI.Text(m.From, 12.5, true));
                col.Children.Add(UI.Text(m.Subject, 12, false, true));
                g.Children.Add(col);
                var t = UI.Text(Links.Ago(m.Date), 11, false, true); Grid.SetColumn(t, 1); t.Margin = new Thickness(8, 0, 0, 0);
                g.Children.Add(t);
                _list.Children.Add(Links.Clickable(UI.Card(g, 8), () => Links.Open(web)));
            }
            if (st.Unread > 8) _list.Children.Add(UI.Text($"… и ещё {st.Unread - 8}", 11, false, true));
        }
        if (Hub.Mail.Busy) _list.Children.Add(UI.Text("Проверяю почту…", 11, false, true));
    }
}

// ═════════════ окно «Добавить ящик» ═════════════
public class MailAddWindow : Window
{
    static MailAddWindow _inst;
    readonly TextBox _email = UI.Input();
    readonly PasswordBox _pass = new();
    readonly TextBlock _help = UI.Text("", 12, false, true), _status = UI.Text("", 12);
    readonly StackPanel _linkHost = new();
    Button _go;

    public static void ShowIt()
    {
        _inst ??= new MailAddWindow();
        _inst.Show(); _inst.Activate();
    }

    MailAddWindow()
    {
        Title = "Добавить почту — Островок";
        Width = 420; SizeToContent = SizeToContent.Height; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "Bg");

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(UI.Text("📧 Добавить почтовый ящик", 16, true));
        p.Children.Add(Label("Адрес почты"));
        p.Children.Add(_email);
        p.Children.Add(Label("Пароль для приложений"));
        _pass.Padding = new Thickness(8, 6, 8, 6); _pass.BorderThickness = new Thickness(0); _pass.FontSize = 13;
        _pass.SetResourceReference(BackgroundProperty, "Card"); _pass.SetResourceReference(ForegroundProperty, "Fg");
        p.Children.Add(_pass);
        _help.TextWrapping = TextWrapping.Wrap; _help.TextTrimming = TextTrimming.None; _help.Margin = new Thickness(0, 10, 0, 4);
        p.Children.Add(_help);
        p.Children.Add(_linkHost);
        _status.TextWrapping = TextWrapping.Wrap; _status.TextTrimming = TextTrimming.None; _status.Margin = new Thickness(0, 6, 0, 0);
        p.Children.Add(_status);
        _go = UI.Btn("Подключить", () => Connect(), true);
        _go.HorizontalAlignment = HorizontalAlignment.Left; _go.Margin = new Thickness(0, 10, 0, 0);
        p.Children.Add(_go);
        p.Children.Add(Note("Пароль хранится только на этом компьютере в зашифрованном виде. Читается лишь отправитель и тема писем, письма не скачиваются и не отмечаются прочитанными."));
        Content = p;

        _email.TextChanged += (_, _) => UpdateHelp();
        Closed += (_, _) => { _inst = null; };
        UpdateHelp();
    }

    static TextBlock Label(string s) { var t = UI.Text(s, 12, true, true); t.Margin = new Thickness(0, 10, 0, 3); return t; }
    static TextBlock Note(string s) { var t = UI.Text(s, 10.5, false, true); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Margin = new Thickness(0, 12, 0, 0); return t; }

    void UpdateHelp()
    {
        var pr = MailboxService.PresetFor(_email.Text);
        _help.Text = pr.Help;
        _linkHost.Children.Clear();
        if (!string.IsNullOrEmpty(pr.HelpUrl))
        {
            var b = UI.Btn("🔗 Создать пароль для приложений", () => Links.Open(pr.HelpUrl));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            _linkHost.Children.Add(b);
        }
    }

    async void Connect()
    {
        _go.IsEnabled = false;
        _status.Foreground = (Brush)Application.Current.Resources["FgDim"];
        _status.Text = "Проверяю подключение…";
        var err = await Hub.Mail.AddAsync(_email.Text, _pass.Password);
        _go.IsEnabled = true;
        if (err == null) { Close(); return; }
        _status.Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120));
        _status.Text = "⚠ " + err;
    }
}

// ═════════════ ✈️ Telegram ═════════════
public class TelegramTab : TabPage
{
    readonly StackPanel _list = new();
    readonly TextBlock _state = UI.Text("", 12, false, true);

    public TelegramTab()
    {
        var dock = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(btns, Dock.Right);
        btns.Children.Add(UI.Btn("Открыть Telegram", () => TelegramWindow.Open(), true));
        top.Children.Add(btns);
        top.Children.Add(UI.Text("✈️ Telegram", 15, true));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        _state.TextWrapping = TextWrapping.Wrap; _state.TextTrimming = TextTrimming.None; _state.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_state, Dock.Top);
        dock.Children.Add(_state);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Children.Add(dock);
        Hub.Telegram.Changed += () => Dispatcher.BeginInvoke(Rebuild);
        Rebuild();
    }

    public override void OnShown() => Rebuild();

    void Rebuild()
    {
        _list.Children.Clear();
        if (!Hub.S.TelegramPreload)
            _state.Text = "Нажмите «Открыть Telegram», войдите по QR-коду (Telegram на телефоне → Настройки → Устройства) и в настройках Telegram включите уведомления. После этого сообщения будут приходить на остров, даже когда окно закрыто.";
        else if (!TelegramWindow.Running)
            _state.Text = "Telegram запускается в фоне…";
        else
            _state.Text = Hub.Telegram.Unread > 0 ? $"Непрочитанных чатов: {Hub.Telegram.Unread}" : "Работает в фоне. Новых сообщений нет.";

        foreach (var n in Hub.Telegram.Recent.Take(10))
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var col = new StackPanel();
            col.Children.Add(UI.Text(n.Title, 12.5, true));
            col.Children.Add(UI.Text(n.Body, 12, false, true));
            g.Children.Add(col);
            var t = UI.Text(Links.Ago(n.Time), 11, false, true); Grid.SetColumn(t, 1); t.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(t);
            _list.Children.Add(Links.Clickable(UI.Card(g, 8), () => TelegramWindow.Open()));
        }
    }
}
