using System.Windows.Threading;

namespace Ostrovok.Services;

public enum TimerMode { None, Timer, Pomodoro, Stopwatch }

/// <summary>Таймер, помодоро (25/5) и секундомер с кольцом прогресса.</summary>
public class TimerService
{
    public TimerMode Mode { get; private set; } = TimerMode.None;
    public bool Running { get; private set; }
    public bool PomodoroBreak { get; private set; }
    public TimeSpan Total { get; private set; }
    public TimeSpan Elapsed => Running ? _acc + (DateTime.UtcNow - _start) : _acc;
    public TimeSpan Remaining => Mode == TimerMode.Stopwatch ? Elapsed : TimeSpan.FromSeconds(Math.Max(0, (Total - Elapsed).TotalSeconds));
    public double Progress => Mode == TimerMode.Stopwatch || Total.TotalSeconds <= 0 ? 0 : Math.Clamp(Elapsed.TotalSeconds / Total.TotalSeconds, 0, 1);
    public bool Active => Mode != TimerMode.None;

    public event Action Tick;
    public event Action<string> Finished;
    public event Action StateChanged;

    DateTime _start; TimeSpan _acc;
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public TimerService() { _t.Tick += (_, _) => OnTick(); }

    public void StartTimer(TimeSpan d) => Begin(TimerMode.Timer, d);
    public void StartPomodoro() { PomodoroBreak = false; Begin(TimerMode.Pomodoro, TimeSpan.FromMinutes(25)); }
    public void StartStopwatch() => Begin(TimerMode.Stopwatch, TimeSpan.Zero);

    void Begin(TimerMode m, TimeSpan d)
    {
        Mode = m; Total = d; _acc = TimeSpan.Zero; _start = DateTime.UtcNow; Running = true; _t.Start();
        StateChanged?.Invoke();
    }

    public void PauseResume()
    {
        if (!Active) return;
        if (Running) { _acc += DateTime.UtcNow - _start; Running = false; }
        else { _start = DateTime.UtcNow; Running = true; }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        Mode = TimerMode.None; Running = false; _acc = TimeSpan.Zero; _t.Stop();
        StateChanged?.Invoke();
    }

    void OnTick()
    {
        Tick?.Invoke();
        if (Mode == TimerMode.Stopwatch || !Running) return;
        if (Elapsed < Total) return;

        if (Mode == TimerMode.Pomodoro)
        {
            bool wasBreak = PomodoroBreak;
            PomodoroBreak = !PomodoroBreak;
            Finished?.Invoke(wasBreak ? "Перерыв закончился — пора работать" : "Помодоро закончилось — отдохните 5 минут");
            Begin(TimerMode.Pomodoro, TimeSpan.FromMinutes(PomodoroBreak ? 5 : 25));
            return;
        }
        Finished?.Invoke("Таймер сработал");
        Stop();
    }

    public static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}
