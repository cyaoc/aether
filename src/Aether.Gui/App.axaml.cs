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
            var host = CreateHost(DataDirectory.Locate());
            var logger = host.Services.GetRequiredService<ILogger<App>>();
            void LogUiException(object? sender, DispatcherUnhandledExceptionEventArgs args) =>
                logger.LogCritical(args.Exception, "UI 线程未处理异常");
            Dispatcher.UIThread.UnhandledException += LogUiException;
            logger.LogInformation("GUI 已启动");
            desktop.MainWindow = new MainWindow { DataContext = host.Services.GetRequiredService<MainWindowViewModel>() };
            desktop.Exit += (_, _) =>
            {
                Dispatcher.UIThread.UnhandledException -= LogUiException;
                host.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>A settings error does not stop startup; Core refuses the next room connection with it.</summary>
    internal static IHost CreateHost(string dataDirectory)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddAetherFileLogging(dataDirectory, Settings.Load(dataDirectory));
        builder.Services.AddSingleton(services => new AetherClient(
            services.GetRequiredService<ILogger<AetherClient>>(), dataDirectory));
        builder.Services.AddSingleton(services => GuiSettings.Load(
            dataDirectory, services.GetRequiredService<ILogger<GuiSettings>>()));
        builder.Services.AddSingleton<MainWindowViewModel>();
        return builder.Build();
    }
}
