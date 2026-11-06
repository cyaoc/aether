using Aether.Core;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsMacOS()) MacOSDockIcon.Apply();
            var dataDirectory = DataDirectory.Locate();
            var host = CreateHost(dataDirectory);
            var logger = host.Services.GetRequiredService<ILogger<App>>();
            void LogUiException(object? sender, DispatcherUnhandledExceptionEventArgs args) =>
                logger.LogCritical(args.Exception, "UI 线程未处理异常");
            Dispatcher.UIThread.UnhandledException += LogUiException;
            logger.LogInformation("GUI 已启动");
            var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(dataDirectory) { DataContext = viewModel };
            desktop.MainWindow = window;
            var restart = false;
            viewModel.UpdateInstalled += () =>
            {
                restart = true;
                window.Close();
            };
            desktop.Exit += (_, _) =>
            {
                Dispatcher.UIThread.UnhandledException -= LogUiException;
                // As late as it can be, so the new version's clean-up finds the old files let go.
                if (restart) host.Services.GetRequiredService<AutoUpdater>().Restart();
                host.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static IHost CreateHost(string dataDirectory)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.AddAether(dataDirectory);
        builder.Services.AddSingleton(services => GuiSettings.Load(
            dataDirectory, services.GetRequiredService<ILogger<GuiSettings>>()));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(services => AutoUpdater.ForThisProgram(
            services.GetRequiredService<ILogger<AutoUpdater>>(), services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<MainWindowViewModel>();
        return builder.Build();
    }
}
