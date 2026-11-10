using Aether.Core;
using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class DesktopPetSettingsTests
{
    [Theory]
    [InlineData("desktop_pet.enabled", "true")]
    [InlineData("desktop_pet.shows_off", "[entry]")]
    [InlineData("desktop_pet.big_gift_spend", "20000")]
    [InlineData("desktop_pet.sleepy_minutes", "0")]
    [InlineData("desktop_pet.daily_max_seconds", "60")]
    [InlineData("desktop_pet.mishap_percent", "0")]
    [InlineData("desktop_pet.lines.1.bigGift", """["谢谢{昵称}"]""")]
    public Task A_desktop_pet_setting_changed_in_the_file_reaches_the_web_service(string name, string value) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.SaveSettings();
        var viewModel = harness.CreateViewModel();
        try
        {
            await viewModel.WebService.RefreshWebServiceAsync(Settings.Load(harness.DataDirectory));
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { [name] = value });
            var edited = Settings.Load(harness.DataDirectory);
            await viewModel.WebService.RefreshWebServiceAsync(edited);
            Assert.Same(edited, harness.Client.WebService.Settings);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
