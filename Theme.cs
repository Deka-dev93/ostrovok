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
        // Палитра и «плитки» — по образцу Atoll (github.com/Fuwni/dynamic-island-windows, MIT)
        r["Fg"] = B(dark ? "#FFFFFFFF" : "#FF1C1C1E");
        r["FgInv"] = B(dark ? "#FF000000" : "#FFFFFFFF");
        r["FgDim"] = B(dark ? "#FFB4B4BA" : "#FF5E5E64");
        r["Fg3"] = B(dark ? "#FF7C7C84" : "#FF98989E");
        r["TrackBg"] = B(dark ? "#38FFFFFF" : "#1E000000");
        r["ChipBg"] = B(dark ? "#26FFFFFF" : "#12000000");
        r["PlayBg"] = B(dark ? "#FFFFFFFF" : "#FF1C1C1E");
        r["PlayFg"] = B(dark ? "#FF000000" : "#FFFFFFFF");
        r["TileStroke"] = dark ? Grad(0.5, 0, 0.5, 1, ("#2AFFFFFF", 0), ("#08FFFFFF", 1)) : Grad(0.5, 0, 0.5, 1, ("#FFFFFFFF", 0), ("#14000000", 1));
        r["Card"] = dark ? Grad(0.5, 0, 0.5, 1, ("#1EFFFFFF", 0), ("#0CFFFFFF", 1)) : Grad(0.5, 0, 0.5, 1, ("#E8FFFFFF", 0), ("#A8FFFFFF", 1));
        r["CardHover"] = dark ? Grad(0.5, 0, 0.5, 1, ("#30FFFFFF", 0), ("#18FFFFFF", 1)) : B("#FFFFFFFF");
        r["SwitchOn"] = Grad(0.5, 0, 0.5, 1, ("#4AE06F", 0), ("#27C24F", 1));
        if (glass)
        {
            // «стекло»: полупрозрачная заливка с лёгким градиентом, яркая кромка и блик сверху — и в тёмном, и в светлом варианте
            r["Bg"] = dark ? Grad(0, 0, 0.35, 1, ("#CC2C2C3A", 0), ("#B8131319", 1)) : Grad(0, 0, 0.35, 1, ("#DCFFFFFF", 0), ("#BDE6EAF5", 1));
            r["Edge"] = Grad(0, 0, 1, 1, (dark ? "#8CFFFFFF" : "#FFFFFFFF", 0), ("#16FFFFFF", 0.4), ("#0AFFFFFF", 0.62), (dark ? "#59FFFFFF" : "#B3FFFFFF", 1));
            r["Sheen"] = Grad(0, 0, 0, 1, (dark ? "#38FFFFFF" : "#80FFFFFF", 0), ("#0CFFFFFF", 0.45), ("#00FFFFFF", 1));
        }
        else
        {
            // сплошной остров; «прозрачный низ» — к нижнему краю заливка чуть просвечивает, как в Atoll
            bool fade = Hub.S.BottomFade;
            r["Bg"] = dark
                ? (fade ? Grad(0.5, 0, 0.5, 1, ("#FF000000", 0), ("#FF000000", 0.45), ("#C8000000", 1)) : B("#FF000000"))
                : (fade ? Grad(0.5, 0, 0.5, 1, ("#FFF6F6F9", 0), ("#FFF6F6F9", 0.45), ("#D2F6F6F9", 1)) : B("#FFF6F6F9"));
            r["Edge"] = dark ? Grad(0.5, 0, 0.5, 0.7, ("#24FFFFFF", 0), ("#06FFFFFF", 1)) : B("#16000000");
            r["Sheen"] = Brushes.Transparent;
        }
        var a = new SolidColorBrush(AccentColor); a.Freeze();
        r["Accent"] = a;
        // фон окна настроек: как сам остров — чёрный сверху, к низу светлеет
        r["WinBg"] = dark ? Grad(0.5, 0, 0.5, 1, ("#FF000000", 0), ("#FF0B0B0D", 0.55), ("#FF2A2A2E", 1)) : Grad(0.5, 0, 0.5, 1, ("#FFF6F6F9", 0), ("#FFE6E6EC", 1));
        r["SideBg"] = Brushes.Transparent;
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
        var b = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(pad), Child = child, Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "TileStroke");
        return b;
    }

    /// <summary>Большая «плитка» как в Atoll: градиентная заливка, тонкая светлая кромка, радиус 20.</summary>
    public static Border Tile(UIElement child, double padX = 14, double padY = 12)
    {
        var b = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(padX, padY, padX, padY), Child = child, BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "TileStroke");
        return b;
    }

    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Icon(string glyph, double size = 15, string brushKey = "Fg")
    {
        var t = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);

    /// <summary>Широкая кнопка действия: цветной кружок со значком + подпись (верхний ряд «Главной» в Atoll).</summary>
    public static Button ActionPill(string glyph, string label, string c1, string c2, Action click)
    {
        var chip = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Margin = new Thickness(0, 0, 12, 0),
            Background = new LinearGradientBrush(Hex(c1), Hex(c2), 90) };
        var shine = new Border { CornerRadius = new CornerRadius(19),
            Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Hex("#40FFFFFF"), 0), new GradientStop(Hex("#00FFFFFF"), 0.55), new GradientStop(Hex("#1A000000"), 1) }, 90) };
        var ic = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        chip.Child = new Grid { Children = { shine, ic } };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(chip);
        var t = Text(label, 13.5); sp.Children.Add(t);
        var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("TileBtn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 8, 12, 8) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Круглая кнопка с цветным значком на тонированном круге и подписью снизу (второй ряд «Главной» в Atoll).</summary>
    public static Button RoundAction(string glyph, string label, string color, Action click)
    {
        var c = Hex(color);
        var circle = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(25), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)) };
        circle.Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 18, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel();
        sp.Children.Add(circle);
        var t = Text(label, 11, false, true); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(t);
        var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("BareBtn"), ToolTip = label };
        b.Click += (_, _) => click();
        return b;
    }
}

/// <summary>Базовый класс вкладки.</summary>
public abstract class TabPage : Grid
{
    public virtual void OnShown() { }
    public virtual void Tick() { }
}
