using Aether.Core;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<AetherClient>();
            builder.Services.AddSingleton(services => GuiSettings.Load(services.GetRequiredService<ILogger<GuiSettings>>()));
            builder.Services.AddSingleton<MainWindowViewModel>();
            var host = builder.Build();
            desktop.MainWindow = new MainWindow { DataContext = host.Services.GetRequiredService<MainWindowViewModel>() };
            desktop.Exit += (_, _) => host.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
