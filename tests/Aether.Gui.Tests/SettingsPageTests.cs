using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsPageTests
{
    [Fact]
    public Task Every_core_setting_is_edited_by_exactly_one_settings_section() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        SettingsPage[] pages =
        [
            new BlindBoxSettingsPage(harness.DataDirectory),
            new WelcomeSettingsPage(harness.DataDirectory),
            new WelcomeAnimationSettingsPage(harness.DataDirectory),
            new SendSettingsPage(harness.DataDirectory),
            new GeneralSettingsPage(harness.DataDirectory)
        ];
        Assert.Equal(Settings.AllDefinitions.Select(definition => definition.Name).Order(),
            pages.SelectMany(page => page.FieldNames).Order());
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public Task Welcome_lists_show_three_rows_until_expanded(bool uids, int count) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), WelcomeListYaml(uids, count), (page, _) =>
        {
            GuiHarness.Flush();
            var fold = page.FindControl<Button>(uids ? "UidFold" : "WelcomeTextFold");
            Assert.NotNull(fold);
            Assert.Equal(count > 3, fold.IsVisible);
            Assert.Equal(3, WelcomeListRows(page, uids).Count(row => row.IsShown));
            if (count == 3) return;
            Assert.Equal(uids ? "还有 1 位" : "还有 1 条", AutomationProperties.GetName(fold));
            var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(fold));
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
            fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal(4, WelcomeListRows(page, uids).Count(row => row.IsShown));
            Assert.Equal(uids ? "收起，只看前 3 位" : "收起，只看前 3 条", AutomationProperties.GetName(fold));
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
            provider.Collapse();
            GuiHarness.Flush();
            Assert.Equal(3, WelcomeListRows(page, uids).Count(row => row.IsShown));
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
        });

    private static string WelcomeListYaml(bool uids, int count) => "welcome:\n  " + (uids
        ? $"uids: [{string.Join(", ", Enumerable.Range(1, count))}]"
        : $"texts: {Settings.TextListText(Enumerable.Range(1, count).Select(i => $"欢迎{i}"))}") + "\n";

    private static EditableRow[] WelcomeListRows(SettingsPage page, bool uids) => uids
        ? Rows(page).Where(row => row.Kind == WelcomeCondition.Uid).ToArray()
        : WelcomeTextList.Rows(page);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Collapsed_welcome_lists_report_only_hidden_errors(bool uids) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), WelcomeListYaml(uids, 5), (page, _) =>
        {
            GuiHarness.Flush();
            var fold = page.FindControl<Button>(uids ? "UidFold" : "WelcomeTextFold")!;
            var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(fold));
            provider.Expand();
            var rows = WelcomeListRows(page, uids);
            foreach (var index in new[] { 0, 3, 4 })
                GuiHarness.FindRowControl<TextBox>(page, rows[index], uids ? "UidInput" : "ValueInput").Text = "";
            GuiHarness.Flush();
            provider.Collapse();
            GuiHarness.Flush();
            Assert.Equal(uids ? "还有 2 位，其中 2 位有错" : "还有 2 条，其中 2 条有错", AutomationProperties.GetName(fold));
            Assert.Contains("danger", fold.Classes);
            Assert.True(fold.TryFindResource("Danger", fold.ActualThemeVariant, out var danger));
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(danger).Color, Assert.IsAssignableFrom<ISolidColorBrush>(fold.Foreground).Color);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);

            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(page, rows[0], "RemoveButton"))).Invoke();
            GuiHarness.Flush();
            Assert.Equal(uids ? "还有 1 位，其中 1 位有错" : "还有 1 条，其中 1 条有错", AutomationProperties.GetName(fold));
            Assert.True(rows[3].IsShown);
            provider.Expand();
            GuiHarness.Flush();
            Assert.DoesNotContain("danger", fold.Classes);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, rows[4], "ErrorMessage").IsVisible);
            GuiHarness.FindRowControl<TextBox>(page, rows[4], uids ? "UidInput" : "ValueInput").Text = uids ? "42" : "欢迎";
            GuiHarness.Flush();
            provider.Collapse();
            Assert.Equal(uids ? "还有 1 位" : "还有 1 条", AutomationProperties.GetName(fold));
            Assert.DoesNotContain("danger", fold.Classes);
        });

    [Fact]
    public Task Fold_button_keeps_its_tone_when_hovered() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), WelcomeListYaml(false, 4), (page, _) =>
        {
            GuiHarness.Flush();
            var fold = page.FindControl<Button>("WelcomeTextFold")!;
            var window = (Window)TopLevel.GetTopLevel(page)!;
            window.MouseMove(fold.TranslatePoint(new Point(10, 10), window)!.Value);
            GuiHarness.Flush();
            Assert.Equal(Resource("Accent.Text"), Shown());
            WelcomeTextList.Input(page, 3).Text = "";
            GuiHarness.Flush();
            Assert.Contains("danger", fold.Classes);
            Assert.Equal(Resource("Danger"), Shown());

            Color Shown() => Assert.IsAssignableFrom<ISolidColorBrush>(fold.GetVisualDescendants().OfType<ContentPresenter>().First().Foreground).Color;
            Color Resource(string key) =>
                Assert.IsAssignableFrom<ISolidColorBrush>(fold.TryFindResource(key, fold.ActualThemeVariant, out var brush) ? brush : null).Color;
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Adding_to_a_collapsed_welcome_list_expands_and_focuses_the_new_row(bool uids) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), WelcomeListYaml(uids, 20), (page, _) =>
        {
            GuiHarness.Flush();
            var fold = page.FindControl<Button>(uids ? "UidFold" : "WelcomeTextFold")!;
            if (uids) Add(page, WelcomeCondition.Uid);
            else page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var rows = WelcomeListRows(page, uids);
            Assert.Equal(21, rows.Length);
            Assert.All(rows, row => Assert.True(row.IsShown));
            var input = GuiHarness.FindRowControl<TextBox>(page, rows[^1], uids ? "UidInput" : "ValueInput");
            Assert.True(input.IsFocused);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, rows[^1], "ErrorMessage").IsVisible);
            var scroll = input.GetVisualAncestors().OfType<ScrollViewer>().First();
            var top = input.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, 0, scroll.Bounds.Height - input.Bounds.Height);
            Assert.Equal(ExpandCollapseState.Expanded, Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(fold)).ExpandCollapseState);
        });

    [Fact]
    public Task Uid_group_disappears_when_empty_and_menu_reuses_the_group() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42, 43]\n  guard: 关闭\n", (page, _) =>
        {
            GuiHarness.Flush();
            var group = page.FindControl<StackPanel>("UidGroup");
            Assert.NotNull(group);
            Assert.Equal("1 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            var first = GuiHarness.FindRowControl<TextBox>(page, Rows(page)[0], "UidInput")!;
            var second = GuiHarness.FindRowControl<TextBox>(page, Rows(page)[1], "UidInput")!;
            Assert.Equal(first.TranslatePoint(default, page)!.Value.X, second.TranslatePoint(default, page)!.Value.X);
            Remove(page, Rows(page)[0]);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, Rows(page)[0], "TypeName")!.IsVisible);
            Remove(page, Rows(page)[0]);
            Assert.False(group.IsVisible);
            Assert.True(page.FindControl<Grid>("EmptyConditions")!.IsVisible);
            Add(page, WelcomeCondition.Uid);
            Add(page, WelcomeCondition.Uid);
            Assert.True(group.IsVisible);
            Assert.Equal(2, Rows(page).Length);
            Assert.Equal("1 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            Assert.All(Rows(page), row => Assert.Contains(group, GuiHarness.FindRowControl<TextBox>(page, row, "UidInput").GetVisualAncestors()));
            Assert.All(Rows(page), row => Assert.False(GuiHarness.FindRowControl<Grid>(page, row, "OrSeparator")!.IsVisible));
        });

    [Fact]
    public Task Inserting_into_a_hidden_welcome_text_reveals_the_last_edited_row() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), WelcomeListYaml(false, 4), (page, _) =>
        {
            var fold = page.FindControl<Button>("WelcomeTextFold")!;
            var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(fold));
            provider.Expand();
            GuiHarness.Flush();
            WelcomeTextList.Input(page, 3).Focus();
            GuiHarness.Flush();
            Assert.True(WelcomeTextList.Input(page, 3).IsFocused);
            PressAndRelease(page, fold);
            Assert.False(WelcomeTextList.Rows(page)[3].IsShown);
            page.FindControl<Button>("InsertNicknameButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Contains("{昵称}", WelcomeTextList.Input(page, 3).Text);
            Assert.True(WelcomeTextList.Rows(page)[3].IsShown);
            Assert.True(WelcomeTextList.Input(page, 3).IsFocused);
        });

    [Fact]
    public Task New_welcome_text_waits_for_blur_then_validates_while_editing() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var add = page.FindControl<Button>("AddWelcomeTextButton");
            Assert.NotNull(add);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal("2 条", page.FindControl<TextBlock>("WelcomeTextCount")!.Text);
            var input = WelcomeTextList.Input(page, 1);
            var error = WelcomeTextList.Error(page, 1);
            Assert.True(input.IsFocused);
            Assert.False(error.IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.False(WelcomeTextList.Error(page).IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            foreach (var name in new[] { "InsertAtButton", "InsertNicknameButton", "InsertGuardButton" })
            {
                var button = page.FindControl<Button>(name)!;
                button.BringIntoView();
                GuiHarness.Flush();
                var window = (Window)TopLevel.GetTopLevel(page)!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                GuiHarness.Flush();
                Assert.True(input.IsFocused);
                Assert.Contains(button.Tag!.ToString()!, input.Text);
                input.Text = " ";
                GuiHarness.Flush();
                Assert.False(error.IsVisible);
                Assert.DoesNotContain("invalid", input.Classes);
            }
            WelcomeTextList.Input(page).Focus();
            GuiHarness.Flush();
            Assert.Equal("填上欢迎语，或删掉这条", error.Text);
            Assert.True(error.IsVisible);
            Assert.Contains("invalid", input.Classes);
            AssertDangerBorder(input);
            input.Focus();
            input.Text = "欢迎回家";
            GuiHarness.Flush();
            Assert.False(error.IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            input.Text = "{@}";
            GuiHarness.Flush();
            Assert.True(error.IsVisible);
            Assert.Equal("欢迎语不能只有 {@}，还要写上发出去的文字。", error.Text);
            AssertDangerBorder(input);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    /// <summary>The invalid outline wins over the focus outline too.</summary>
    private static void AssertDangerBorder(TextBox input)
    {
        var border = input.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
        Assert.True(input.TryFindResource("Danger", input.ActualThemeVariant, out var danger));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(danger).Color, Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color);
    }

    [Fact]
    public Task Add_buttons_still_click_when_leaving_a_new_row_pushes_them_down() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var addText = page.FindControl<Button>("AddWelcomeTextButton")!;
            addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            PressAndRelease(page, addText);
            Assert.True(WelcomeTextList.Error(page, 1).IsVisible);
            Assert.Equal(3, WelcomeTextList.Rows(page).Length);

            var uid = Add(page, WelcomeCondition.Uid);
            Assert.True(GuiHarness.FindRowControl<TextBox>(page, uid, "UidInput")!.IsFocused);
            PressAndRelease(page, page.FindControl<Button>("AddConditionButton")!);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, uid, "ErrorMessage")!.IsVisible);
            Assert.True(Menu(page).IsOpen);
        });

    /// <summary>A mouse click near the button's top edge, letting layout run between press and release as it does on screen.</summary>
    private static void PressAndRelease(SettingsPage page, Button button)
    {
        button.BringIntoView();
        GuiHarness.Flush();
        var window = (Window)TopLevel.GetTopLevel(page)!;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        GuiHarness.Flush();
        window.MouseUp(point, MouseButton.Left);
        GuiHarness.Flush();
    }

    [Theory]
    [InlineData("", "填上欢迎语，或删掉这条")]
    [InlineData(" \t", "填上欢迎语，或删掉这条")]
    [InlineData("{@}", "欢迎语不能只有 {@}，还要写上发出去的文字。")]
    public Task Existing_welcome_text_validates_without_waiting_for_blur(string value, string message) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var input = WelcomeTextList.Input(page);
            input.Focus();
            input.Text = value;
            GuiHarness.Flush();
            Assert.Equal(message, WelcomeTextList.Error(page).Text);
            Assert.True(WelcomeTextList.Error(page).IsVisible);
            Assert.Contains("invalid", input.Classes);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            input.Text = "欢迎回来";
            GuiHarness.Flush();
            Assert.False(WelcomeTextList.Error(page).IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Existing_uid_validates_without_waiting_for_blur() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42]\n", (page, _) =>
        {
            var row = Row(page, WelcomeCondition.Uid);
            var input = GuiHarness.FindRowControl<TextBox>(page, row, "UidInput")!;
            input.Focus();
            input.Text = "0";
            GuiHarness.Flush();
            Assert.Equal("UID 必须是正整数", GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.Text);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.Contains("invalid", input.Classes);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            input.Text = "43";
            GuiHarness.Flush();
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Welcome_rows_save_in_order_and_removing_back_to_the_original_list_clears_the_draft() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, path) =>
        {
            var before = File.ReadAllText(path);
            var first = Assert.Single(WelcomeTextList.Rows(page));
            var remove = GuiHarness.FindRowControl<Button>(page, first, "RemoveButton");
            Assert.False(remove.IsEnabled);
            Assert.Single(WelcomeTextList.Rows(page));
            Assert.Equal("写多条时，每次随机发一条", page.FindControl<TextBlock>("WelcomeRandomHint")!.Text);
            Assert.False(page.FindControl<TextBlock>("WelcomeInsertTarget")!.IsVisible);

            page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            WelcomeTextList.Input(page, 1).Text = "欢迎{舰长}";
            WelcomeTextList.Input(page).Focus();
            GuiHarness.Flush();
            Assert.True(WelcomeTextList.Error(page, 1).IsVisible);
            Assert.False(WelcomeTextList.Error(page).IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            WelcomeTextList.Input(page, 1).Text = "{@}欢迎\"老朋友\"\\回家,{{花}}";
            GuiHarness.Flush();
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            Assert.Equal("每次随机发一条", page.FindControl<TextBlock>("WelcomeRandomHint")!.Text);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(page, WelcomeTextList.Rows(page)[1], "RemoveButton"))).Invoke();
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            Assert.Equal(before, File.ReadAllText(path));

            page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            WelcomeTextList.Input(page, 1).Text = "{@}欢迎\"老朋友\"\\回家,{{花}}";
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.Equal(["欢迎{大航海}{昵称}", "{@}欢迎\"老朋友\"\\回家,{{花}}"], settings.WelcomeTexts.Value);
            Assert.True(settings.WelcomeTexts.CanEdit);
            Assert.Contains("  texts: [\"欢迎{大航海}{昵称}\", \"{@}欢迎\\\"老朋友\\\"\\\\回家,{{花}}\"]", File.ReadAllText(path));
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Insert_buttons_remember_the_last_edited_row_and_follow_it_after_deletion() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  texts: [欢迎, 回家, 晚上好]\n", (page, _) =>
        {
            var input = WelcomeTextList.Input(page, 1);
            input.Focus();
            input.CaretIndex = 1;
            var insert = page.FindControl<Button>("InsertNicknameButton")!;
            Assert.True(insert.Focus(NavigationMethod.Tab));
            var window = (Window)TopLevel.GetTopLevel(page)!;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            GuiHarness.Flush();
            Assert.True(input.IsFocused);
            Assert.Equal("回{昵称}家", input.Text);
            Assert.Equal("欢迎", WelcomeTextList.Input(page).Text);
            Assert.Equal("到第 2 条", page.FindControl<TextBlock>("WelcomeInsertTarget")!.Text);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(page, WelcomeTextList.Rows(page)[0], "RemoveButton"))).Invoke();
            Assert.Equal("到第 1 条", page.FindControl<TextBlock>("WelcomeInsertTarget")!.Text);
            page.FindControl<Button>("InsertAtButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("{@}回{昵称}家", input.Text);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(page, WelcomeTextList.Rows(page)[0], "RemoveButton"))).Invoke();
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("{昵称}", WelcomeTextList.Input(page).Text);
            Assert.False(page.FindControl<TextBlock>("WelcomeInsertTarget")!.IsVisible);
        });

    [Fact]
    public Task Clicking_another_rows_remove_button_keeps_the_insert_target() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  texts: [欢迎, 回家, 晚上好]\n", (page, _) =>
        {
            WelcomeTextList.Input(page).Focus();
            var remove = GuiHarness.FindRowControl<Button>(page, WelcomeTextList.Rows(page)[2], "RemoveButton");
            remove.BringIntoView();
            GuiHarness.Flush();
            // A pointer press focuses the button before its command removes the row.
            var window = (Window)TopLevel.GetTopLevel(page)!;
            var point = remove.TranslatePoint(new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            GuiHarness.Flush();
            Assert.Equal(["欢迎", "回家"], WelcomeTextList.Rows(page).Select(row => row.Value));
            Assert.Equal("到第 1 条", page.FindControl<TextBlock>("WelcomeInsertTarget")!.Text);
        });

    [Fact]
    public Task Saving_only_welcome_texts_validates_the_reloaded_rows_again() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  texts: [欢迎]\n  guard: 关闭\n", (page, _) =>
        {
            Assert.Empty(ConditionList.Rows(page));
            WelcomeTextList.Input(page).Text = "欢迎回家";
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var message = GuiHarness.FindVisual<TextBlock>(page, "Message");
            Assert.Equal("已保存。", message.Text);
            // The second pass clears the footer message, as it does when conditions are loaded too.
            GuiHarness.Flush();
            Assert.Equal("", message.Text);
        });

    [Theory]
    [InlineData("\n    - 欢迎\n    - '{@}欢迎回家'")]
    [InlineData("[欢迎,\n    '{@}欢迎回家']")]
    public Task Multiline_welcome_texts_are_all_read_only_and_other_settings_can_be_saved(string value) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), $"welcome:\n  enabled: false\n  texts: {value}\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.Equal(2, WelcomeTextList.Rows(page).Length);
            Assert.All(WelcomeTextList.Rows(page), row =>
            {
                Assert.True(GuiHarness.FindRowControl<TextBox>(page, row, "ValueInput").IsReadOnly);
                Assert.False(GuiHarness.FindRowControl<Button>(page, row, "RemoveButton").IsVisible);
            });
            Assert.True(page.FindControl<TextBlock>("WelcomeTextReadOnly")!.IsVisible);
            Assert.False(page.FindControl<Button>("AddWelcomeTextButton")!.IsVisible);
            Assert.False(page.FindControl<TextBlock>("WelcomeInsertTarget")!.IsVisible);
            foreach (var name in new[] { "InsertAtButton", "InsertNicknameButton", "InsertGuardButton" })
                Assert.False(page.FindControl<Button>(name)!.IsEnabled);
            page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            page.FindControl<Button>("InsertAtButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, WelcomeTextList.Rows(page).Length);
            Assert.Equal("欢迎", WelcomeTextList.Input(page).Text);
            page.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked = true;
            GuiHarness.Flush();
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("welcome:\n  enabled: false", "welcome:\n  enabled: true"), File.ReadAllText(path));
        });

    [Fact]
    public Task Default_template_shows_only_the_guard_condition() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var rows = page.FindControl<ItemsControl>("ConditionRows");
            Assert.NotNull(rows);
            var row = Assert.Single(((WelcomeSettingsPage)page).WelcomeThresholds);
            Assert.Equal("大航海身份", row.Name);
            var guard = GuiHarness.FindRowControl<ComboBox>(page, row, "GuardInput");
            Assert.Equal("舰长", guard.SelectedItem);
            Assert.Equal(new[] { "舰长", "提督", "总督" }, guard.Items.Cast<string>());
            Assert.Equal("1 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Theory]
    [InlineData("关闭")]
    [InlineData("舰长")]
    [InlineData("提督")]
    [InlineData("总督")]
    public Task Welcome_fields_display_and_save_only_changed_lines(string guard) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "# 保留\r\nwelcome:\r\n  enabled: false # 开关\r\n  texts: ['欢迎{昵称}'] # 正文\r\n  max_length: 20 # 上限\r\n  uids: [42] # 老朋友\r\n  fan_club_level: 1 # 老粉\r\n  guard: 舰长 # 档位\r\n  wealth_level: 2 # 荣耀\r\nfuture: 保留\r\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var enabled = window.FindControl<ToggleSwitch>("WelcomeInput");
            var text = WelcomeTextList.Input(window);
            var uids = GuiHarness.FindRowControl<TextBox>(window, Row(window, WelcomeCondition.Uid), "UidInput");
            var fanClub = GuiHarness.FindRowControl<NumericUpDown>(window, Row(window, WelcomeCondition.FanClubLevel), "LevelInput");
            var threshold = GuiHarness.FindRowControl<ComboBox>(window, Row(window, WelcomeCondition.Guard), "GuardInput");
            var wealth = GuiHarness.FindRowControl<NumericUpDown>(window, Row(window, WelcomeCondition.WealthLevel), "LevelInput");
            Assert.NotNull(enabled);
            Assert.NotNull(text);
            Assert.NotNull(uids);
            Assert.NotNull(fanClub);
            Assert.NotNull(threshold);
            Assert.NotNull(wealth);
            Assert.False(enabled.IsChecked);
            Assert.True(enabled.IsEnabled);
            Assert.Equal("下次连接直播间时生效", GuiHarness.FindVisual<TextBlock>(window, "TimingHint").Text);
            Assert.Equal("欢迎{昵称}", text.Text);
            Assert.False(text.IsReadOnly);
            Assert.Equal(["42"], UidLabels(window));
            Assert.False(uids.IsReadOnly);
            Assert.Equal("1", fanClub.Text);
            Assert.Equal("2", wealth.Text);
            foreach (var number in new[] { fanClub, wealth })
            {
                Assert.False(number.IsReadOnly);
                Assert.True(number.AllowSpin);
            }
            Assert.Equal(new[] { "舰长", "提督", "总督" }, threshold.Items.Cast<string>());
            Assert.Equal("舰长", threshold.SelectedItem);
            Assert.True(threshold.IsEnabled);

            enabled.IsChecked = true;
            text.Text = "{@}欢迎{大航海}{昵称}回家";
            SetUids(window, "2147483648", "9223372036854775807");
            fanClub.Text = "21";
            if (guard == "关闭") Remove(window, Row(window, WelcomeCondition.Guard));
            else threshold.SelectedItem = guard;
            wealth.Text = "45";
            Dispatcher.UIThread.RunJobs();
            var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: false # 开关", "enabled: true # 开关")
                .Replace("texts: ['欢迎{昵称}']", "texts: [\"{@}欢迎{大航海}{昵称}回家\"]")
                .Replace("uids: [42]", "uids: [2147483648, 9223372036854775807]")
                .Replace("fan_club_level: 1", "fan_club_level: 21")
                .Replace("guard: 舰长", $"guard: {guard}")
                .Replace("wealth_level: 2", "wealth_level: 45"), File.ReadAllText(path));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.True(settings.WelcomeEnabled.Value);
            Assert.Equal("{@}欢迎{大航海}{昵称}回家", settings.WelcomeTexts.Value[0]);
            Assert.Contains("welcome.max_length", settings.UnknownKeys);
            Assert.Equal([2147483648L, long.MaxValue], settings.WelcomeUids.Value);
            Assert.Equal(21, settings.WelcomeFanClubLevel.Value);
            Assert.Equal(guard, settings.WelcomeGuard.Value);
            Assert.Equal(45, settings.WelcomeWealthLevel.Value);
        });

    [Fact]
    public Task Add_menu_inserts_defaults_in_setting_order_and_disables_existing_thresholds() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var button = page.FindControl<Button>("AddConditionButton");
            Assert.NotNull(button);
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.Equal(4, menu.Items.Count);
            Assert.False(MenuItem(WelcomeCondition.Guard).IsEnabled);
            Assert.Contains(Assert.IsAssignableFrom<Control>(MenuItem(WelcomeCondition.Guard).Header).GetVisualDescendants()
                .OfType<TextBlock>(), text => text.Text == "已添加" && text.IsVisible);
            Add(WelcomeCondition.WealthLevel);
            Add(WelcomeCondition.Uid);
            Add(WelcomeCondition.FanClubLevel);
            Add(WelcomeCondition.Uid);
            var rows = Rows(page);
            Assert.Equal(new[] { WelcomeCondition.Uid, WelcomeCondition.Uid, WelcomeCondition.FanClubLevel, WelcomeCondition.Guard, WelcomeCondition.WealthLevel }, rows.Select(row => row.Kind));
            Assert.Equal(new[] { "", "", "1", "舰长", "10" }, rows.Select(row => row.Value));
            Assert.True(MenuItem(WelcomeCondition.Uid).IsEnabled);
            Assert.All(menu.Items.Cast<MenuItem>().Skip(1), item => Assert.False(item.IsEnabled));
            Assert.True(GuiHarness.FindRowControl<TextBox>(page, rows[1], "UidInput")!.IsFocused);
            Assert.Equal("4 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            Assert.False(GuiHarness.FindRowControl<Grid>(page, rows[0], "OrSeparator")!.IsVisible);
            Assert.False(GuiHarness.FindRowControl<Grid>(page, rows[1], "OrSeparator")!.IsVisible);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, rows[0], "TypeName")!.IsVisible);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, rows[1], "TypeName")!.IsVisible);
            Assert.All(rows.Skip(2), row => Assert.True(GuiHarness.FindRowControl<Grid>(page, row, "OrSeparator")!.IsVisible));

            MenuItem MenuItem(WelcomeCondition kind) => menu.Items.Cast<MenuItem>().Single(item => Equals(item.Tag, kind));
            void Add(WelcomeCondition kind)
            {
                menu.ShowAt(button);
                GuiHarness.Flush();
                MenuItem(kind).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                GuiHarness.Flush();
                Assert.False(menu.IsOpen);
                var row = Rows(page).Last(row => row.Kind == kind);
                Assert.True(GuiHarness.FindRowControl<Control>(page, row,
                    kind == WelcomeCondition.Uid ? "UidInput" : kind == WelcomeCondition.Guard ? "GuardInput" : "LevelInput").IsKeyboardFocusWithin);
            }
        });

    [Fact]
    public Task Welcome_condition_accessible_names_keep_their_kind_after_removal() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  guard: 关闭\n", (page, _) =>
        {
            Add(page, WelcomeCondition.Uid);
            Add(page, WelcomeCondition.FanClubLevel);
            Add(page, WelcomeCondition.Guard);
            Add(page, WelcomeCondition.WealthLevel);
            Add(page, WelcomeCondition.Uid);
            var rows = Rows(page);
            Assert.Equal(5, rows.Length);
            Assert.IsAssignableFrom<IInvokeProvider>(
                ControlAutomationPeer.CreatePeerForElement(GuiHarness.FindRowControl<Button>(page, rows[0], "RemoveButton")!)).Invoke();
            GuiHarness.Flush();
            rows = Rows(page);
            Assert.Equal(4, rows.Length);
            Assert.Equal(["指定观众 UID", "粉丝团等级门槛", "大航海身份", "荣耀等级门槛"],
                rows.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<Control>(page, row,
                    row.Kind == WelcomeCondition.Uid ? "UidInput" : row.Kind == WelcomeCondition.Guard ? "GuardInput" : "LevelInput")!)));
            Assert.Equal(["删除指定观众 UID", "删除粉丝团等级条件", "删除大航海身份条件", "删除荣耀等级条件"],
                rows.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<Button>(page, row, "RemoveButton")!)));
        });

    [Fact]
    public Task Removing_all_conditions_saves_disabled_values_and_shows_empty_state() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42, 43] # 观众\n  fan_club_level: 1 # 粉丝团\n  guard: 舰长 # 大航海\n  wealth_level: 10 # 荣耀\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.Equal(new[] { WelcomeCondition.Uid, WelcomeCondition.Uid, WelcomeCondition.FanClubLevel, WelcomeCondition.Guard, WelcomeCondition.WealthLevel }, Rows(page).Select(row => row.Kind));
            foreach (var row in Rows(page)) Remove(page, row);
            Assert.True(page.FindControl<Grid>("EmptyConditions")!.IsVisible);
            Assert.False(page.FindControl<TextBlock>("ConditionCount")!.IsVisible);
            Assert.True(page.FindControl<Button>("AddConditionButton")!.IsVisible);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("uids: [42, 43]", "uids: []").Replace("fan_club_level: 1", "fan_club_level: 0")
                .Replace("guard: 舰长", "guard: 关闭").Replace("wealth_level: 10", "wealth_level: 0"), File.ReadAllText(path));
            Assert.Empty(Rows(page));
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
        });

    [Fact]
    public Task Removing_and_restoring_equivalent_conditions_is_not_a_change() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42]\n  fan_club_level: 1\n  guard: 舰长\n  wealth_level: 10\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            foreach (var row in Rows(page)) Remove(page, row);
            Add(page, WelcomeCondition.WealthLevel);
            Add(page, WelcomeCondition.Guard);
            Add(page, WelcomeCondition.FanClubLevel);
            GuiHarness.FindRowControl<TextBox>(page, Add(page, WelcomeCondition.Uid), "UidInput")!.Text = "0042";
            GuiHarness.Flush();
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            Assert.Equal(before, File.ReadAllText(path));
        });

    [Fact]
    public Task A_uid_removed_and_added_back_after_the_others_is_not_a_change() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42, 43]\n", (page, _) =>
        {
            Remove(page, Rows(page)[0]);
            GuiHarness.FindRowControl<TextBox>(page, Add(page, WelcomeCondition.Uid), "UidInput")!.Text = "42";
            GuiHarness.Flush();
            Assert.Equal(["43", "42"], UidLabels(page));
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Duplicate_uids_in_the_file_are_reported_without_making_a_draft() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42, 42]\n", (page, path) =>
        {
            Assert.Equal("和上面那条重复了", GuiHarness.FindRowControl<TextBlock>(page, Rows(page)[1], "ErrorMessage")!.Text);
            Assert.False(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            File.WriteAllText(path, "welcome:\n  uids: [42]\n");
            page.RefreshFileStatus(Settings.Load(Path.GetDirectoryName(path)!));
            Assert.Equal(["42"], UidLabels(page));
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, Rows(page)[0], "ErrorMessage")!.IsVisible);
        });

    [Theory]
    [InlineData("", "填一个 UID，或删掉这条")]
    [InlineData(" ", "填一个 UID，或删掉这条")]
    [InlineData("+123", "UID 必须是正整数")]
    [InlineData("0", "UID 必须是正整数")]
    [InlineData("-1", "UID 必须是正整数")]
    [InlineData("1.5", "UID 必须是正整数")]
    [InlineData("abc", "UID 必须是正整数")]
    [InlineData("12345, 67890", "UID 必须是正整数")]
    [InlineData("[0]", "UID 必须是正整数")]
    [InlineData("9223372036854775808", "UID 必须是正整数")]
    [InlineData("0042", "和上面那条重复了")]
    public Task Invalid_uid_is_reported_on_its_row_and_can_be_corrected(string value, string message) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  uids: [42]\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            var originalRow = Row(page, WelcomeCondition.Uid);
            var row = Add(page, WelcomeCondition.Uid);
            var input = GuiHarness.FindRowControl<TextBox>(page, row, "UidInput")!;
            Assert.True(input.IsFocused);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            input.Text = value;
            GuiHarness.Flush();
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            GuiHarness.FindRowControl<TextBox>(page, originalRow, "UidInput").Focus();
            GuiHarness.Flush();
            Assert.Equal(message, GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.Text);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.Contains("invalid", input.Classes);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, originalRow, "ErrorMessage")!.IsVisible);
            Assert.True(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            var save = GuiHarness.FindVisual<Button>(page, "PART_SaveButton");
            Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
            input.Focus();
            input.Text = "43";
            GuiHarness.Flush();
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.DoesNotContain("invalid", input.Classes);
            Assert.True(save.IsEnabled);
            input.Text = value;
            GuiHarness.Flush();
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.Contains("invalid", input.Classes);
            Assert.False(save.IsEnabled);
            input.Text = "9223372036854775807";
            GuiHarness.Flush();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("uids: [42]", "uids: [42, 9223372036854775807]"), File.ReadAllText(path));
        });

    [Fact]
    public Task Removing_the_first_duplicate_clears_the_remaining_rows_error() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            SetUids(page, "42", "42");
            var rows = Rows(page).Where(row => row.Kind == WelcomeCondition.Uid).ToArray();
            GuiHarness.FindRowControl<TextBox>(page, rows[0], "UidInput").Focus();
            GuiHarness.Flush();
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, rows[1], "ErrorMessage")!.IsVisible);
            Remove(page, rows[0]);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, rows[1], "ErrorMessage")!.IsVisible);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
        });

    [Theory]
    [InlineData(WelcomeCondition.FanClubLevel, "0")]
    [InlineData(WelcomeCondition.WealthLevel, "0")]
    [InlineData(WelcomeCondition.FanClubLevel, "-1")]
    [InlineData(WelcomeCondition.WealthLevel, "-1")]
    [InlineData(WelcomeCondition.FanClubLevel, "1.5")]
    [InlineData(WelcomeCondition.WealthLevel, "")]
    [InlineData(WelcomeCondition.WealthLevel, "abc")]
    [InlineData(WelcomeCondition.WealthLevel, "2147483648")]
    public Task Invalid_level_is_reported_on_its_row_and_can_be_corrected(WelcomeCondition kind, string value) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, path) =>
        {
            var before = File.ReadAllText(path);
            var row = Add(page, kind);
            var number = GuiHarness.FindRowControl<NumericUpDown>(page, row, "LevelInput")!;
            var input = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            input.Text = value;
            GuiHarness.Flush();
            Assert.Equal("至少 1 级", GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.Text);
            var save = GuiHarness.FindVisual<Button>(page, "PART_SaveButton");
            Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
            input.Text = "21";
            GuiHarness.Flush();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var key = kind == WelcomeCondition.FanClubLevel ? "fan_club_level" : "wealth_level";
            Assert.Equal(before.Replace(key + ": 0", key + ": 21"), File.ReadAllText(path));
        });

    [Fact]
    public Task Read_only_disabled_conditions_cannot_be_added() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome: {uids: [], fan_club_level: 0, guard: 关闭, wealth_level: 0}\n", (page, _) =>
        {
            Assert.Empty(Rows(page));
            Assert.True(page.FindControl<Grid>("EmptyConditions")!.IsVisible);
            var hint = page.FindControl<TextBlock>("ConditionsReadOnly")!;
            Assert.True(hint.IsVisible);
            Assert.Equal("指定观众、粉丝团等级、大航海身份、荣耀等级是多行写法，请在设置文件里改", hint.Text);
            Assert.All(Menu(page).Items.Cast<MenuItem>(), item =>
            {
                Assert.False(item.IsEnabled);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            });
            Assert.Empty(Rows(page));
        });

    [Fact]
    public Task A_multiline_threshold_without_a_row_is_named_and_cannot_be_added() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  wealth_level: |-\n    0\n", (page, _) =>
        {
            var hint = page.FindControl<TextBlock>("ConditionsReadOnly")!;
            Assert.True(hint.IsVisible);
            Assert.Equal("荣耀等级是多行写法，请在设置文件里改", hint.Text);
            Assert.Equal(new[] { true, true, false, false },
                Menu(page).Items.Cast<MenuItem>().Select(item => item.IsEnabled));
            Remove(page, Row(page, WelcomeCondition.Guard));
            Assert.True(Menu(page).Items.Cast<MenuItem>().Single(item => Equals(item.Tag, WelcomeCondition.Guard)).IsEnabled);
            Assert.Equal("荣耀等级是多行写法，请在设置文件里改", hint.Text);
        });

    [Fact]
    public Task A_multiline_threshold_is_read_only_while_other_conditions_can_be_saved() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n  fan_club_level: |-\n    21\n  guard: 舰长\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            var row = Row(page, WelcomeCondition.FanClubLevel);
            Assert.True(GuiHarness.FindRowControl<NumericUpDown>(page, row, "LevelInput")!.IsReadOnly);
            Assert.False(GuiHarness.FindRowControl<NumericUpDown>(page, row, "LevelInput")!.AllowSpin);
            Assert.False(GuiHarness.FindRowControl<Button>(page, row, "RemoveButton")!.IsVisible);
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, row, "ReadOnlyMessage")!.IsVisible);
            Assert.False(page.FindControl<TextBlock>("ConditionsReadOnly")!.IsVisible);
            Remove(page, row);
            Assert.Same(row, Row(page, WelcomeCondition.FanClubLevel));
            Remove(page, Row(page, WelcomeCondition.Guard));
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("guard: 舰长", "guard: 关闭"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData("欢迎{舰长}")]
    [InlineData("欢迎{@}")]
    [InlineData("欢迎{昵称")]
    [InlineData("欢迎}")]
    public Task Invalid_welcome_placeholders_disable_save_and_can_be_corrected(string text) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = WelcomeTextList.Input(window);
            input.Text = text;
            Dispatcher.UIThread.RunJobs();
            var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
            Assert.False(save.IsEnabled);
            Assert.Equal("可用占位符为 {@}（只能放开头）、{昵称}、{大航海}；花括号本身用 {{ 和 }}。", WelcomeTextList.Error(window).Text);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(text, input.Text);

            input.Text = "{@}欢迎{{{昵称}}}";
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("{@}欢迎{{{昵称}}}", Settings.Load(Path.GetDirectoryName(path)!).WelcomeTexts.Value[0]);
        });

    [Fact]
    public Task Long_welcome_text_can_be_saved_before_the_accounts_limit_is_known() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (window, path) =>
        {
            WelcomeTextList.Input(window).Text = "一二三四五六七八九十百";
            Dispatcher.UIThread.RunJobs();
            var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.Equal("一二三四五六七八九十百", settings.WelcomeTexts.Value[0]);
        });

    [Fact]
    public Task Welcome_defaults_and_help_are_shown_and_missing_values_can_be_saved() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome:\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.False(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked);
            Assert.Equal("欢迎{大航海}{昵称}", WelcomeTextList.Input(window).Text);
            Assert.Null(window.FindControl<NumericUpDown>("WelcomeMaxLengthInput"));
            Assert.Empty(UidLabels(window));
            Assert.DoesNotContain(Rows(window), row => row.Kind == WelcomeCondition.FanClubLevel);
            Assert.Equal("舰长", GuiHarness.FindRowControl<ComboBox>(window, Row(window, WelcomeCondition.Guard), "GuardInput")!.SelectedItem);
            Assert.DoesNotContain(Rows(window), row => row.Kind == WelcomeCondition.WealthLevel);
            var hint = window.FindControl<TextBlock>("WelcomeTextHint")!.Text;
            Assert.Contains("英文、数字、半角符号算 1，其他字符算 2", hint);
            Assert.Contains("上限自动读取账号的", hint);
            Assert.Contains("{@}（只能放开头）、{昵称}、{大航海}", hint);
            Assert.Contains("{{ 和 }}", hint);
            SetUids(window, "12345", "67890");
            Dispatcher.UIThread.RunJobs();
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var after = File.ReadAllText(path);
            Assert.Contains("  uids: [12345, 67890]\n", after);
            Assert.Equal(before, after.Replace("  uids: [12345, 67890]\n", ""));
            Assert.Equal([12345L, 67890L], Settings.Load(Path.GetDirectoryName(path)!).WelcomeUids.Value);
        });

    [Fact]
    public Task Flow_welcome_group_is_read_only_and_preserved_when_other_settings_are_saved() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), "welcome: {enabled: true, texts: ['欢迎{昵称}'], uids: [12345, 67890], fan_club_level: 21, guard: 提督, wealth_level: 45}\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked);
            Assert.False(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsEnabled);
            Assert.Equal("欢迎{昵称}", WelcomeTextList.Input(window).Text);
            Assert.True(WelcomeTextList.Input(window).IsReadOnly);
            Assert.Equal(["12345", "67890"], UidLabels(window));
            Assert.All(Rows(window).Where(row => row.Kind == WelcomeCondition.Uid), row => Assert.True(GuiHarness.FindRowControl<TextBox>(window, row, "UidInput")!.IsReadOnly));
            Assert.Equal("提督", GuiHarness.FindRowControl<ComboBox>(window, Row(window, WelcomeCondition.Guard), "GuardInput")!.SelectedItem);
            Assert.False(GuiHarness.FindRowControl<ComboBox>(window, Row(window, WelcomeCondition.Guard), "GuardInput")!.IsEnabled);
            foreach (var (name, value) in new[] { (WelcomeCondition.FanClubLevel, "21"), (WelcomeCondition.WealthLevel, "45") })
            {
                var number = GuiHarness.FindRowControl<NumericUpDown>(window, Row(window, name), "LevelInput")!;
                Assert.Equal(value, number.Text);
                Assert.True(number.IsReadOnly);
                Assert.False(number.AllowSpin);
            }
            foreach (var name in new[] { "Welcome", "WelcomeText" })
                Assert.True(window.FindControl<TextBlock>(name + "ReadOnly")!.IsVisible);
            Assert.All(Rows(window), row =>
            {
                Assert.Equal(row == Rows(window).First(r => r.Kind == row.Kind), GuiHarness.FindRowControl<TextBlock>(window, row, "ReadOnlyMessage")!.IsVisible);
                Assert.False(GuiHarness.FindRowControl<Button>(window, row, "RemoveButton")!.IsVisible);
            });
            Assert.All(Menu(window).Items.Cast<MenuItem>(), item => Assert.False(item.IsEnabled));
            var general = new GeneralSettingsPage(Path.GetDirectoryName(path)!);
            ((Window)TopLevel.GetTopLevel(window)!).Content = general;
            GuiHarness.Flush();
            general.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            Dispatcher.UIThread.RunJobs();
            var save = GuiHarness.FindVisual<Button>(general, "PART_SaveButton");
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Information", "level: Warning"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData(null)]
    [InlineData("  announce: true # 开关\n")]
    public Task Announcement_switch_defaults_on_and_saves_only_its_line(string? line) =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "blind_box:\n  enabled: true\n" + line, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("AnnounceInput");
            Assert.NotNull(input);
            Assert.True(input.IsChecked);
            Assert.True(input.IsEnabled);
            Assert.Equal("下次连接直播间时生效", GuiHarness.FindVisual<TextBlock>(window, "TimingHint").Text);
            input.IsChecked = false;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(line is null ? before.Replace("  enabled: true\n", "  enabled: true\n  announce: false\n")
                : before.Replace("announce: true", "announce: false"), File.ReadAllText(path));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxAnnounce.Value);
        });

    [Fact]
    public Task Multiline_announcement_switch_is_read_only_and_preserved() =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "blind_box:\n  announce: |-\n    false\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("AnnounceInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("AnnounceReadOnly")!.IsVisible);
            window.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true", "enabled: false"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_tally_and_send_queue_fields_load_defaults_validate_and_save_missing_settings() =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "# 保留\nblind_box:\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("DailyKeywordInput");
            var monthly = window.FindControl<TextBox>("MonthlyKeywordInput");
            var send = new SendSettingsPage(Path.GetDirectoryName(path)!);
            var interval = send.FindControl<NumericUpDown>("SendIntervalInput");
            Assert.NotNull(keyword);
            Assert.NotNull(monthly);
            Assert.NotNull(interval);
            Assert.Equal("今日盲盒", keyword.Text);
            Assert.Equal("本月盲盒", monthly.Text);
            Assert.Equal("1", interval.Text);
            var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
            keyword.Text = " ";
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.Contains("blind_box.daily_keyword", window.FindControl<TextBlock>("DailyKeywordError")!.Text);
            keyword.Text = "查盲盒";
            monthly.Text = " ";
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.Contains("blind_box.monthly_keyword", window.FindControl<TextBlock>("MonthlyKeywordError")!.Text);
            monthly.Text = "查月盲盒";
            Dispatcher.UIThread.RunJobs();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true\n", "enabled: true\n  daily_keyword: 查盲盒\n  monthly_keyword: 查月盲盒\n"), File.ReadAllText(path));
            ((Window)TopLevel.GetTopLevel(window)!).Content = send;
            GuiHarness.Flush();
            save = GuiHarness.FindVisual<Button>(send, "PART_SaveButton");
            var numberText = Assert.Single(interval.GetVisualDescendants().OfType<TextBox>());
            numberText.Text = "1.5";
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.Contains("send.interval_seconds", send.FindControl<TextBlock>("SendIntervalError")!.Text);
            numberText.Text = "7";
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true\n", "enabled: true\n  daily_keyword: 查盲盒\n  monthly_keyword: 查月盲盒\n")
                .Replace("interval_seconds: 1", "interval_seconds: 7"), File.ReadAllText(path));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Equal("查盲盒", settings.BlindBoxDailyKeyword.Value);
            Assert.Equal("查月盲盒", settings.BlindBoxMonthlyKeyword.Value);
            Assert.Equal(7, settings.SendIntervalSeconds.Value);
        });

    [Fact]
    public Task Multiline_blind_box_tally_and_send_queue_fields_are_read_only_and_preserved() =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "blind_box:\n  daily_keyword: |-\n    查盲盒\n  monthly_keyword: |-\n    查月盲盒\nsend:\n  interval_seconds: |-\n    7\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("DailyKeywordInput");
            var send = new SendSettingsPage(Path.GetDirectoryName(path)!);
            var interval = send.FindControl<NumericUpDown>("SendIntervalInput");
            Assert.NotNull(keyword);
            Assert.NotNull(interval);
            Assert.True(keyword.IsReadOnly);
            Assert.Equal("查盲盒", keyword.Text);
            Assert.True(interval.IsReadOnly);
            Assert.False(interval.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("DailyKeywordReadOnly")!.IsVisible);
            var monthly = window.FindControl<TextBox>("MonthlyKeywordInput")!;
            Assert.True(monthly.IsReadOnly);
            Assert.Equal("查月盲盒", monthly.Text);
            Assert.True(window.FindControl<TextBlock>("MonthlyKeywordReadOnly")!.IsVisible);
            Assert.True(send.FindControl<TextBlock>("SendIntervalReadOnly")!.IsVisible);
            window.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("\nsend:", "\n  enabled: false\nsend:"), File.ReadAllText(path));
        });

    [Fact]
    public Task Multiline_blind_box_switch_is_read_only_and_preserved_when_saving() =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "blind_box:\n  enabled: |-\n    false\nlog:\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("BlindBoxInput")!;
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("BlindBoxReadOnly")!.IsVisible);
            var general = new GeneralSettingsPage(Path.GetDirectoryName(path)!);
            ((Window)TopLevel.GetTopLevel(window)!).Content = general;
            GuiHarness.Flush();
            general.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            GuiHarness.FindVisual<Button>(general, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Debug", "level: Warning"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_switch_loads_changes_and_saves_without_touching_other_lines() =>
        WithSettingsPage(dir => new BlindBoxSettingsPage(dir), "# 保留\nblind_box:\n  enabled: false # 开关\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("BlindBoxInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            input.IsChecked = true;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: false # 开关", "enabled: true # 开关"), File.ReadAllText(path));
            Assert.True(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
            Assert.StartsWith("已保存", GuiHarness.FindVisual<TextBlock>(window, "Message").Text);
            Assert.Equal("下次连接直播间时生效", GuiHarness.FindVisual<TextBlock>(window, "TimingHint").Text);
            input.IsChecked = false;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
        });

    [Fact]
    public Task Multiline_level_is_read_only() =>
        WithSettingsPage(dir => new GeneralSettingsPage(dir), "log:\n  level: |-\n    Debug\n", (window, _) =>
        {
            Assert.False(window.FindControl<ComboBox>("LogLevelInput")!.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("LogLevelReadOnly")!.IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(window, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Save_rereads_external_changes_and_refreshes_the_form() =>
        WithSettingsPage(dir => new GeneralSettingsPage(dir), null, (window, path) =>
        {
            File.WriteAllText(path, "# 外部修改\nlog:\n  retention_days: 30\n  level: Warning\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "7";
            var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, window.FindControl<ComboBox>("LogLevelInput")!.SelectedItem);
            var after = File.ReadAllText(path);
            Assert.StartsWith("# 外部修改\nlog:\n  retention_days: 7\n  level: Warning\n", after);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(after, File.ReadAllText(path));

            File.WriteAllText(path, "log:\n  level: @bad\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(save.IsEnabled);
            Assert.Contains("第 2 行", GuiHarness.FindVisual<TextBlock>(window, "FileMessage").Text);
            Assert.Equal("log:\n  level: @bad\n", File.ReadAllText(path));
        });

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public Task Invalid_input_immediately_shows_core_validation_and_disables_save(string input) =>
        WithSettingsPage(dir => new GeneralSettingsPage(dir), null, (window, _) =>
        {
            var number = window.FindControl<NumericUpDown>("RetentionInput")!;
            var textBox = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            textBox.Text = input;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Settings.Validate("log.retention_days", input)!, window.FindControl<TextBlock>("RetentionError")!.Text);
            Assert.False(GuiHarness.FindVisual<Button>(window, "PART_SaveButton").IsEnabled);
            textBox.Text = "14";
            Dispatcher.UIThread.RunJobs();
            Assert.True(GuiHarness.FindVisual<Button>(window, "PART_SaveButton").IsEnabled);
        });

    [Theory]
    [InlineData("log:\n  level: @bad\n", "第 2 行")]
    [InlineData("log:\n  retention_days: 0\n", "log.retention_days")]
    [InlineData("log:\n  !!str level: Debug\n  level: Warning\n", "第 3 行（log.level）：设置名重复")]
    [InlineData("!!str log:\n  level: Debug\nlog:\n  retention_days: 7\n", "第 3 行（log）：分组名重复")]
    public Task File_errors_display_core_location_and_disable_save(string yaml, string expected) =>
        WithSettingsPage(dir => new GeneralSettingsPage(dir), yaml, (window, path) =>
        {
            var message = GuiHarness.FindVisual<TextBlock>(window, "FileMessage");
            Assert.True(message.IsVisible);
            Assert.Contains(path, message.Text);
            Assert.Contains(expected, message.Text);
            Assert.False(GuiHarness.FindVisual<Button>(window, "PART_SaveButton").IsEnabled);
        });

    [Fact]
    public Task Multiline_settings_are_read_only_and_do_not_block_editing_other_settings() =>
        WithSettingsPage(dir => new GeneralSettingsPage(dir), "log:\n  retention_days: |-\n    7\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<NumericUpDown>("RetentionInput")!.IsReadOnly);
            Assert.False(window.FindControl<NumericUpDown>("RetentionInput")!.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("RetentionReadOnly")!.IsVisible);
            window.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            GuiHarness.FindVisual<Button>(window, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Debug", "level: Warning"), File.ReadAllText(path));
        });

    [Fact]
    public Task Main_window_shows_general_settings_and_saves_only_the_changed_line() =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            Directory.CreateDirectory(harness.DataDirectory);
            var path = Settings.FilePath(harness.DataDirectory);
            const string yaml = "# 我的设置\nlog:\n  retention_days: '7' # 保留\n  level: 'Debug' # 级别\n  future: yes\n";
            File.WriteAllText(path, yaml);
            var viewModel = harness.CreateViewModel();
            var main = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
            main.Show();
            try
            {
                main.FindControl<ListBox>("Navigation")!.SelectedIndex = 6;
                GuiHarness.Flush();
                var window = main.GetVisualDescendants().OfType<SettingsPage>().Single(page => page.IsEffectivelyVisible);
                Assert.Empty(main.OwnedWindows);
                var before = File.ReadAllText(path);
                Assert.Contains("原样保留", window.FindControl<TextBlock>("UnknownKeysMessage")!.Text);
                window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
                Dispatcher.UIThread.RunJobs();
                var save = GuiHarness.FindVisual<Button>(window, "PART_SaveButton");
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(before.Replace("retention_days: '7'", "retention_days: 14"), File.ReadAllText(path));
                Assert.Equal(14, Settings.Load(harness.DataDirectory).LogRetentionDays.Value);

            }
            finally
            {
                await viewModel.ShutdownAsync();
                main.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, TestContext.Current.CancellationToken);

    private static WelcomeConditionRow Row(SettingsPage page, WelcomeCondition kind) => Rows(page).First(row => row.Kind == kind);
    private static IEnumerable<string?> UidLabels(SettingsPage page) => Rows(page)
        .Where(row => row.Kind == WelcomeCondition.Uid).Select(row => row.Uid);

    private static void SetUids(SettingsPage page, params string[] values)
    {
        foreach (var row in Rows(page).Where(row => row.Kind == WelcomeCondition.Uid)) Remove(page, row);
        foreach (var value in values) GuiHarness.FindRowControl<TextBox>(page, Add(page, WelcomeCondition.Uid), "UidInput")!.Text = value;
        GuiHarness.Flush();
    }

    [Fact]
    public Task Welcome_text_accessible_names_follow_the_remaining_rows_after_removal() =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var add = page.FindControl<Button>("AddWelcomeTextButton")!;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var rows = WelcomeTextList.Rows(page);
            Assert.Equal(3, rows.Length);
            Assert.IsAssignableFrom<IInvokeProvider>(
                ControlAutomationPeer.CreatePeerForElement(GuiHarness.FindRowControl<Button>(page, rows[0], "RemoveButton"))).Invoke();
            GuiHarness.Flush();
            rows = WelcomeTextList.Rows(page);
            Assert.Equal(2, rows.Length);
            Assert.Equal(["第 1 条欢迎语", "第 2 条欢迎语"],
                rows.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<TextBox>(page, row, "ValueInput"))));
            Assert.Equal(["删除第 1 条欢迎语", "删除第 2 条欢迎语"],
                rows.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<Button>(page, row, "RemoveButton"))));
        });

    [Theory]
    [InlineData("InsertNicknameButton", 1, 2, "欢迎{昵称}回家", 6)]
    [InlineData("InsertAtButton", 1, 3, "{@}欢迎回家", 3)]
    public Task Welcome_placeholder_insertion_collapses_the_selection(
        string buttonName, int selectionStart, int selectionEnd, string expectedText, int expectedCaret) =>
        WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, _) =>
        {
            var text = WelcomeTextList.Input(page);
            text.Text = "欢迎回家";
            text.Focus();
            text.CaretIndex = selectionEnd;
            text.SelectionStart = selectionStart;
            text.SelectionEnd = selectionEnd;
            page.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal(expectedText, text.Text);
            Assert.Equal(expectedCaret, text.CaretIndex);
            Assert.Equal(expectedCaret, text.SelectionStart);
            Assert.Equal(expectedCaret, text.SelectionEnd);
            Assert.True(text.IsFocused);
        });

    [Fact]
    public Task Welcome_insert_buttons_use_caret_and_at_viewer_is_inserted_only_once() => WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, path) =>
    {
        var text = WelcomeTextList.Input(page);
        text.Text = "欢迎回家";
        text.CaretIndex = 2;
        Click("InsertNicknameButton");
        Assert.Equal("欢迎{昵称}回家", text.Text);
        Assert.Equal(6, text.CaretIndex);
        Click("InsertGuardButton");
        Assert.Equal("欢迎{昵称}{大航海}回家", text.Text);
        Click("InsertAtButton");
        Click("InsertAtButton");
        Assert.Equal("{@}欢迎{昵称}{大航海}回家", text.Text);

        void Click(string name)
        {
            var button = page.FindControl<Button>(name);
            Assert.NotNull(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    });

    [Fact]
    public Task Escaped_at_text_does_not_block_inserting_an_at_placeholder() => WithSettingsPage(dir => new WelcomeSettingsPage(dir), null, (page, path) =>
    {
        var text = WelcomeTextList.Input(page);
        text.Text = "欢迎{{@}}";
        var button = page.FindControl<Button>("InsertAtButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("{@}欢迎{{@}}", text.Text);
        Assert.Null(Settings.Validate("welcome.texts", Settings.TextListText([text.Text!])));
    });

    private static Task WithSettingsPage(Func<string, SettingsPage> createPage, string? yaml, Action<SettingsPage, string> test) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var path = Settings.FilePath(harness.DataDirectory);
            if (yaml is not null)
            {
                Directory.CreateDirectory(harness.DataDirectory);
                File.WriteAllText(path, yaml);
            }
            var page = createPage(harness.DataDirectory);
            var window = new Window { Content = page, Width = 736, Height = 640 };
            window.Show();
            try { test(page, path); }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
}
