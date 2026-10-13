using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class ApplicationIconTests
{
    [Fact]
    public Task Main_and_logout_windows_have_an_application_icon() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var main = new MainWindow(harness.DataDirectory);
        var logout = new ConfirmLogoutWindow();
        main.Show();
        logout.Show(main);
        try
        {
            Assert.NotNull(main.Icon);
            Assert.NotNull(logout.Icon);
        }
        finally { logout.Close(); main.Close(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
