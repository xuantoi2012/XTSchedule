using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using XTSchedule.Core.Interfaces;
using XTSchedule.Core.Services;
using XTSchedule.UI.ViewModels;
using XTSchedule.UI.Views;

namespace XTSchedule.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceProvider = ConfigureServices();
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "XTSchedule", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScheduleDocumentService, ScheduleDocumentService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services.BuildServiceProvider();
    }
}
