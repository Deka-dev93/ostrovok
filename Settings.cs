using System.IO;
using System.Text.Json;

namespace Ostrovok;

public class NoteItem { public DateTime Created { get; set; } = DateTime.Now; public string Text { get; set; } = ""; }
public class ReminderItem { public DateTime When { get; set; } public string Text { get; set; } = ""; public bool Fired { get; set; } }

public class AppSettings
{
    public string City { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string Character { get; set; } = "Fox";
    public string Accent { get; set; } = "#7C5CFF";
    public bool DarkTheme { get; set; } = true;
    public bool Glass { get; set; } = false;
    public List<string> Dock { get; set; } = new() { "explorer.exe", "notepad.exe", "calc.exe", "mspaint.exe", "powershell.exe", "taskmgr.exe" };
    public string HolidayMode { get; set; } = "always";      // date | always | off
    public bool HolidaySnowScreen { get; set; } = true;
    public bool HolidaySnowCap { get; set; } = true;
    public string HolidayGarland { get; set; } = "Blink";    // Off | Blink | Wave | Music
    public int SnowAmount { get; set; } = 100;
    public string HolidayChar { get; set; } = "Snowman";
    public List<string> HiddenTabs { get; set; } = new();
    public List<string> TabOrder { get; set; } = new();
    public bool ShowWeatherInPill { get; set; } = true;
    public bool ShowCharacter { get; set; } = true;
    public bool PopMail { get; set; } = true;
    public bool PopTelegram { get; set; } = true;
    public bool PopHeadphones { get; set; } = true;
    public bool PopSystem { get; set; } = true;
    public bool OpenOnClick { get; set; } = false;
    public bool EdgeMode { get; set; } = false;
    public bool ShowLyricsInPill { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool Autostart { get; set; } = false;
    public string ChatProvider { get; set; } = "claude";
    public string IcsUrl { get; set; } = "";
    public bool TelegramPreload { get; set; } = false;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public List<NoteItem> Notes { get; set; } = new();
    public List<ReminderItem> Reminders { get; set; } = new();
    public List<string> Favorites { get; set; } = new();

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ostrovok");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    public static string DataDir => Dir;
    public static string LogPath => Path.Combine(Dir, "log.txt");

    public static AppSettings Current { get; private set; } = Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) { Log("settings load: " + ex.Message); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log("settings save: " + ex.Message); }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
