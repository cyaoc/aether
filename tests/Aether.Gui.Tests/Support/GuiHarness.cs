using Aether.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Gui.Tests.Support;

internal sealed class GuiHarness : IDisposable
{
    private readonly AetherClient client;
    private int received;

    public GuiHarness() => client = new AetherClient(new HttpClientHandler(),
        (_, _) => throw new InvalidOperationException("GUI tests must not connect a WebSocket."),
        TimeProvider.System, NullLogger<AetherClient>.Instance, DataDirectory);

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-gui-tests-" + Guid.NewGuid());
    public string SettingsFile => Path.Combine(DataDirectory, "gui.yml");

    public GuiSettings LoadSettings() => GuiSettings.Load(DataDirectory, NullLogger<GuiSettings>.Instance);

    public MainWindowViewModel CreateViewModel() => new(client, NullLogger<MainWindowViewModel>.Instance, LoadSettings());

    /// <summary>Delivers numbered danmaku the way a watch stream would; Content is the running number.</summary>
    public void Receive(MainWindowViewModel viewModel, int count)
    {
        for (var i = 0; i < count; i++)
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "viewer", (received++).ToString()));
    }

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
    }
}
