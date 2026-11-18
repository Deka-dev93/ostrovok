using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ostrovok.Services;

namespace Ostrovok;

/// <summary>Общие сервисы приложения.</summary>
public static class Hub
{
    public static readonly MediaService Media = new();
    public static readonly LyricsService Lyrics = new();
    public static readonly WeatherService Weather = new();
    public static readonly TimerService Timer = new();
    public static readonly ClipboardService Clip = new();
    public static readonly ReminderService Reminders = new();
    public static readonly NotificationService Notices = new();
    public static readonly MailboxService Mail = new();
    public static readonly TelegramService Telegram = new();
    public static readonly HeadphoneService Headphones = new();
    public static AppSettings S => AppSettings.Current;
}

public static class Theme
{
    static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    public static Color AccentColor
    {
        get { try { return (Color)ColorConverter.ConvertFromString(Hub.S.Accent); } catch { return Color.FromRgb(124, 92, 255); } }
    }

    public static readonly string[] Accents = { "#7C5CFF", "#FF5C8A", "#FF9B4A", "#3DDC97", "#4DA3FF", "#F5D547", "#E0E0E0", "#FF5C5C", "#2DD4BF", "#C084FC" };

    public static void Apply()
    {
        var r = Application.Current.Resources;
        bool dark = Hub.S.DarkTheme;
        r["Bg"] = B(dark ? "#F2101013" : "#F5F3F4F8");
        r["Fg"] = B(dark ? "#FFFFFFFF" : "#FF14141A");
        r["FgDim"] = B(dark ? "#A0FFFFFF" : "#8C14141A");
        r["Card"] = B(dark ? "#1FFFFFFF" : "#14000000");
        r["CardHover"] = B(dark ? "#38FFFFFF" : "#26000000");
        var a = new SolidColorBrush(AccentColor); a.Freeze();
        r["Accent"] = a;
    }
}

/// <summary>Мелкие фабрики элементов, чтобы вкладки в коде оставались короткими.</summary>
public static class UI
{
    public static TextBlock Text(string s, double size = 13, bool bold = false, bool dim = false)
    {
        var t = new TextBlock { Text = s, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, dim ? "FgDim" : "Fg");
        return t;
    }

    public static Button Btn(string content, Action click, bool accent = false)
    {
        var b = new Button { Content = content, Style = (Style)Application.Current.FindResource("IconBtn") };
        if (accent) b.SetResourceReference(Control.BackgroundProperty, "Accent");
        b.Click += (_, _) => click();
        return b;
    }

    public static TextBox Input(string tooltip = null)
    {
        var t = new TextBox { Style = (Style)Application.Current.FindResource("DarkText"), ToolTip = tooltip };
        return t;
    }

    public static Border Card(UIElement child, double pad = 10)
    {
        var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(pad), Child = child, Margin = new Thickness(0, 0, 0, 6) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        return b;
    }
}

/// <summary>Базовый класс вкладки.</summary>
public abstract class TabPage : Grid
{
    public virtual void OnShown() { }
    public virtual void Tick() { }
}
