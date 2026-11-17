using System.Net.Http;
using System.Text.Json;

namespace Ostrovok.Services;

public class HourPoint { public string Time = ""; public double Temp; public int Code; public double Precip; }
public class WeatherData
{
    public string City = "";
    public double Temp, Feels, WindSpeed, WindDir, Humidity, Pressure, Uv, Precip;
    public int Code;
    public bool IsDay = true;
    public string Sunrise = "", Sunset = "";
    public List<HourPoint> Hours = new();
    public string Description => WeatherService.Describe(Code);
    public string Icon => WeatherService.IconFor(Code, IsDay);
}

/// <summary>Open-Meteo: без ключей. Геокодинг + прогноз.</summary>
public class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public WeatherData Last;

    public async Task<bool> SetCityAsync(string name)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=1&language=ru");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var r) || r.GetArrayLength() == 0) return false;
            var c = r[0];
            var s = AppSettings.Current;
            s.City = c.GetProperty("name").GetString();
            s.Lat = c.GetProperty("latitude").GetDouble();
            s.Lon = c.GetProperty("longitude").GetDouble();
            s.Save();
            return true;
        }
        catch (Exception ex) { AppSettings.Log("geocode: " + ex.Message); return false; }
    }

    public async Task<WeatherData> RefreshAsync()
    {
        var s = AppSettings.Current;
        if (string.IsNullOrEmpty(s.City)) return null;
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string url = "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={s.Lat.ToString(inv)}&longitude={s.Lon.ToString(inv)}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,pressure_msl,wind_speed_10m,wind_direction_10m,weather_code,is_day,precipitation" +
                "&hourly=temperature_2m,weather_code,precipitation_probability" +
                "&daily=sunrise,sunset,uv_index_max&timezone=auto&forecast_days=2&wind_speed_unit=ms";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement; var cur = root.GetProperty("current");
            var w = new WeatherData
            {
                City = s.City,
                Temp = cur.GetProperty("temperature_2m").GetDouble(),
                Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                Humidity = cur.GetProperty("relative_humidity_2m").GetDouble(),
                Pressure = cur.GetProperty("pressure_msl").GetDouble() * 0.750062,
                WindSpeed = cur.GetProperty("wind_speed_10m").GetDouble(),
                WindDir = cur.GetProperty("wind_direction_10m").GetDouble(),
                Code = cur.GetProperty("weather_code").GetInt32(),
                IsDay = cur.GetProperty("is_day").GetInt32() == 1,
                Precip = cur.GetProperty("precipitation").GetDouble(),
            };
            var d = root.GetProperty("daily");
            w.Sunrise = d.GetProperty("sunrise")[0].GetString()[11..16];
            w.Sunset = d.GetProperty("sunset")[0].GetString()[11..16];
            w.Uv = d.GetProperty("uv_index_max")[0].GetDouble();
            var h = root.GetProperty("hourly");
            var times = h.GetProperty("time"); var temps = h.GetProperty("temperature_2m");
            var codes = h.GetProperty("weather_code"); var pr = h.GetProperty("precipitation_probability");
            var now = DateTime.Now;
            for (int i = 0; i < times.GetArrayLength() && w.Hours.Count < 8; i++)
            {
                var t = DateTime.Parse(times[i].GetString());
                if (t < now.AddMinutes(-59)) continue;
                w.Hours.Add(new HourPoint
                {
                    Time = t.ToString("HH:mm"), Temp = temps[i].GetDouble(), Code = codes[i].GetInt32(),
                    Precip = pr[i].ValueKind == JsonValueKind.Number ? pr[i].GetDouble() : 0
                });
            }
            Last = w;
            return w;
        }
        catch (Exception ex) { AppSettings.Log("weather: " + ex.Message); return Last; }
    }

    public static string Describe(int c) => c switch
    {
        0 => "Ясно", 1 => "Преимущественно ясно", 2 => "Переменная облачность", 3 => "Пасмурно",
        45 or 48 => "Туман", 51 or 53 or 55 => "Морось", 56 or 57 => "Ледяная морось",
        61 => "Небольшой дождь", 63 => "Дождь", 65 => "Сильный дождь", 66 or 67 => "Ледяной дождь",
        71 => "Небольшой снег", 73 => "Снег", 75 => "Сильный снег", 77 => "Снежная крупа",
        80 or 81 or 82 => "Ливень", 85 or 86 => "Снегопад", 95 => "Гроза", 96 or 99 => "Гроза с градом",
        _ => "—"
    };

    public static string IconFor(int c, bool day) => c switch
    {
        0 => day ? "☀️" : "🌙", 1 => day ? "🌤️" : "🌙", 2 => "⛅", 3 => "☁️",
        45 or 48 => "🌫️", 51 or 53 or 55 or 56 or 57 => "🌦️",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️", 95 or 96 or 99 => "⛈️", _ => "🌡️"
    };
}
