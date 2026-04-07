using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SshClient.Services;
using SshClient.ViewModels;

namespace SshClient;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        sc.AddSingleton<ISessionStore, SessionStore>();
        sc.AddTransient<ISshService, SshService>();
        sc.AddTransient<MainViewModel>();
        sc.AddTransient<MainWindow>();
        _services = sc.BuildServiceProvider();

        var win = _services.GetRequiredService<MainWindow>();
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
