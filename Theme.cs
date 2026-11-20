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
    public static readonly VolumeService Volume = new();
    public static readonly ShelfService Shelf = new();
    public static readonly DownloadService Downloads = new();
    public static AppSettings S => AppSettings.Current;
}

public static class Theme
{
    static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    static Brush Grad(double x1, double y1, double x2, double y2, params (string hex, double at)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };
        foreach (var (h, o) in stops) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(h), o));
        b.Freeze();
        return b;
    }

    public static Color AccentColor
    {
        get { try { return (Color)ColorConverter.ConvertFromString(Hub.S.Accent); } catch { return Color.FromRgb(124, 92, 255); } }
    }

    public static readonly string[] Accents = { "#7C5CFF", "#FF5C8A", "#FF9B4A", "#3DDC97", "#4DA3FF", "#F5D547", "#E0E0E0", "#FF5C5C", "#2DD4BF", "#C084FC" };

    public static void Apply()
    {
        var r = Application.Current.Resources;
        bool dark = Hub.S.DarkTheme;
        bool glass = Hub.S.Glass;
        r["Fg"] = B(dark ? "#FFFFFFFF" : "#FF14141A");
        r["FgInv"] = B(dark ? "#FF101013" : "#FFFFFFFF");
        r["FgDim"] = B(dark ? "#B0FFFFFF" : "#8C14141A");
        if (glass)
        {
            // «стекло»: полупрозрачная заливка с лёгким градиентом, яркая кромка и блик сверху — и в тёмном, и в светлом варианте
            r["Bg"] = dark ? Grad(0, 0, 0.35, 1, ("#CC2C2C3A", 0), ("#B8131319", 1)) : Grad(0, 0, 0.35, 1, ("#DCFFFFFF", 0), ("#BDE6EAF5", 1));
            r["Edge"] = Grad(0, 0, 1, 1, (dark ? "#8CFFFFFF" : "#FFFFFFFF", 0), ("#16FFFFFF", 0.4), ("#0AFFFFFF", 0.62), (dark ? "#59FFFFFF" : "#B3FFFFFF", 1));
            r["Sheen"] = Grad(0, 0, 0, 1, (dark ? "#38FFFFFF" : "#80FFFFFF", 0), ("#0CFFFFFF", 0.45), ("#00FFFFFF", 1));
            r["Card"] = B(dark ? "#2BFFFFFF" : "#16000000");
            r["CardHover"] = B(dark ? "#47FFFFFF" : "#28000000");
        }
        else
        {
            r["Bg"] = B(dark ? "#FF050507" : "#F5F3F4F8");
            r["Edge"] = B(dark ? "#2AFFFFFF" : "#22FFFFFF");
            r["Sheen"] = Brushes.Transparent;
            r["Card"] = B(dark ? "#14FFFFFF" : "#14000000");
            r["CardHover"] = B(dark ? "#2AFFFFFF" : "#26000000");
        }
        var a = new SolidColorBrush(AccentColor); a.Freeze();
        r["Accent"] = a;
        // фон окна настроек и боковой панели — меняется вместе с темой (раньше был жёстко задан и текст «пропадал»)
        r["WinBg"] = B(dark ? "#FF0C0C0F" : "#FFF3F4F8");
        r["SideBg"] = B(dark ? "#FF121216" : "#FFE9EBF2");
        Applied?.Invoke();
    }

    /// <summary>Тема применена (смена цвета, тёмная/светлая, стекло).</summary>
    public static event Action Applied;
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
