using System.Threading;
using System.Windows;

namespace Ostrovok;

public partial class App : Application
{
    static Mutex _mutex;
    public static MainWindow Island { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "Ostrovok.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        DispatcherUnhandledException += (_, a) =>
        {
            AppSettings.Log("UI: " + a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => AppSettings.Log("FATAL: " + a.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, a) => { AppSettings.Log("TASK: " + a.Exception); a.SetObserved(); };

        Island = new MainWindow();
        Island.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppSettings.Current.Save();
        base.OnExit(e);
    }
}
