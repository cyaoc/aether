using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsPageTests
{
    [Fact]
    public Task Welcome_texts_can_be_added_and_the_new_empty_row_blocks_save() =>
        WithSettingsPage(SettingsSection.Welcome, null, (page, _) =>
        {
            var add = page.FindControl<Button>("AddWelcomeTextButton");
            Assert.NotNull(add);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal("2 条", page.FindControl<TextBlock>("WelcomeTextCount")!.Text);
            Assert.True(WelcomeTextList.Input(page, 1).IsFocused);
            Assert.Equal("welcome.texts：欢迎语不能为空。", WelcomeTextList.Error(page, 1).Text);
            Assert.False(WelcomeTextList.Error(page).IsVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Welcome_rows_save_in_order_and_removing_back_to_the_original_list_clears_the_draft() =>
        WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
        {
            var before = File.ReadAllText(path);
            var first = Assert.Single(WelcomeTextList.Rows(page));
            var remove = first.FindControl<Button>("RemoveButton")!;
            Assert.False(remove.IsEnabled);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(WelcomeTextList.Rows(page));
            Assert.Equal("写多条时，每次随机发一条", page.FindControl<TextBlock>("WelcomeRandomHint")!.Text);
            Assert.False(page.FindControl<TextBlock>("WelcomeInsertTarget")!.IsVisible);

            page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            WelcomeTextList.Input(page, 1).Text = "欢迎{舰长}";
            GuiHarness.Flush();
            Assert.True(WelcomeTextList.Error(page, 1).IsVisible);
            Assert.False(WelcomeTextList.Error(page).IsVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            WelcomeTextList.Input(page, 1).Text = "{@}欢迎\"老朋友\"\\回家,{{花}}";
            GuiHarness.Flush();
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.Equal("每次随机发一条", page.FindControl<TextBlock>("WelcomeRandomHint")!.Text);
            WelcomeTextList.Rows(page)[1].FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.False(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.Equal(before, File.ReadAllText(path));

            page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            WelcomeTextList.Input(page, 1).Text = "{@}欢迎\"老朋友\"\\回家,{{花}}";
            GuiHarness.Flush();
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.Equal(["欢迎{大航海}{昵称}", "{@}欢迎\"老朋友\"\\回家,{{花}}"], settings.WelcomeTexts.Value);
            Assert.True(settings.WelcomeTexts.CanEdit);
            Assert.Contains("  texts: [\"欢迎{大航海}{昵称}\", \"{@}欢迎\\\"老朋友\\\"\\\\回家,{{花}}\"]", File.ReadAllText(path));
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Insert_buttons_remember_the_last_edited_row_and_follow_it_after_deletion() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  texts: [欢迎, 回家, 晚上好]\n", (page, _) =>
        {
            var input = WelcomeTextList.Input(page, 1);
            input.Focus();
            input.CaretIndex = 1;
            var insert = page.FindControl<Button>("InsertNicknameButton")!;
            insert.Focus();
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("回{昵称}家", input.Text);
            Assert.Equal("欢迎", WelcomeTextList.Input(page).Text);
            Assert.Equal("到第 2 条", page.FindControl<TextBlock>("WelcomeInsertTarget")!.Text);
            WelcomeTextList.Rows(page)[0].FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("到第 1 条", page.FindControl<TextBlock>("WelcomeInsertTarget")!.Text);
            page.FindControl<Button>("InsertAtButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("{@}回{昵称}家", input.Text);
            WelcomeTextList.Rows(page)[0].FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("{昵称}", WelcomeTextList.Input(page).Text);
            Assert.False(page.FindControl<TextBlock>("WelcomeInsertTarget")!.IsVisible);
        });

    [Theory]
    [InlineData("\n    - 欢迎\n    - '{@}欢迎回家'")]
    [InlineData("[欢迎,\n    '{@}欢迎回家']")]
    public Task Multiline_welcome_texts_are_all_read_only_and_other_settings_can_be_saved(string value) =>
        WithSettingsPage(SettingsSection.Welcome, $"welcome:\n  enabled: false\n  texts: {value}\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.Equal(2, WelcomeTextList.Rows(page).Length);
            Assert.All(WelcomeTextList.Rows(page), row =>
            {
                Assert.True(row.FindControl<TextBox>("ValueInput")!.IsReadOnly);
                Assert.False(row.FindControl<Button>("RemoveButton")!.IsVisible);
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
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("welcome:\n  enabled: false", "welcome:\n  enabled: true"), File.ReadAllText(path));
        });

    [Fact]
    public Task Default_template_shows_only_the_guard_condition() =>
        WithSettingsPage(SettingsSection.Welcome, null, (page, _) =>
        {
            var rows = page.FindControl<StackPanel>("ConditionRows");
            Assert.NotNull(rows);
            var row = Assert.Single(rows.Children);
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "大航海身份");
            var guard = Assert.Single(row.GetVisualDescendants().OfType<ComboBox>());
            Assert.Equal("舰长", guard.SelectedItem);
            Assert.Equal(new[] { "舰长", "提督", "总督" }, guard.Items.Cast<string>());
            Assert.Equal("1 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Theory]
    [InlineData("关闭")]
    [InlineData("舰长")]
    [InlineData("提督")]
    [InlineData("总督")]
    public Task Welcome_fields_display_and_save_only_changed_lines(string guard) =>
        WithSettingsPage(SettingsSection.Welcome, "# 保留\r\nwelcome:\r\n  enabled: false # 开关\r\n  texts: ['欢迎{昵称}'] # 正文\r\n  max_length: 20 # 上限\r\n  uids: [42] # 老朋友\r\n  fan_club_level: 1 # 老粉\r\n  guard: 舰长 # 档位\r\n  wealth_level: 2 # 荣耀\r\nfuture: 保留\r\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var enabled = window.FindControl<ToggleSwitch>("WelcomeInput");
            var text = WelcomeTextList.Input(window);
            var uids = Row(window, WelcomeCondition.Uid).FindControl<TextBox>("UidInput");
            var fanClub = Row(window, WelcomeCondition.FanClubLevel).FindControl<NumericUpDown>("LevelInput");
            var threshold = Row(window, WelcomeCondition.Guard).FindControl<ComboBox>("GuardInput");
            var wealth = Row(window, WelcomeCondition.WealthLevel).FindControl<NumericUpDown>("LevelInput");
            Assert.NotNull(enabled);
            Assert.NotNull(text);
            Assert.NotNull(uids);
            Assert.NotNull(fanClub);
            Assert.NotNull(threshold);
            Assert.NotNull(wealth);
            Assert.False(enabled.IsChecked);
            Assert.True(enabled.IsEnabled);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
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
            if (guard == "关闭") Remove(Row(window, WelcomeCondition.Guard));
            else threshold.SelectedItem = guard;
            wealth.Text = "45";
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
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
        WithSettingsPage(SettingsSection.Welcome, null, (page, _) =>
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
            var rows = page.FindControl<StackPanel>("ConditionRows")!.Children.Cast<WelcomeConditionRow>().ToArray();
            Assert.Equal(new[] { WelcomeCondition.Uid, WelcomeCondition.Uid, WelcomeCondition.FanClubLevel, WelcomeCondition.Guard, WelcomeCondition.WealthLevel }, rows.Select(row => row.Kind));
            Assert.Equal(new[] { "", "", "1", "舰长", "10" }, rows.Select(row => row.Value));
            Assert.True(MenuItem(WelcomeCondition.Uid).IsEnabled);
            Assert.All(menu.Items.Cast<MenuItem>().Skip(1), item => Assert.False(item.IsEnabled));
            Assert.True(rows[1].FindControl<TextBox>("UidInput")!.IsFocused);
            Assert.Equal("5 条", page.FindControl<TextBlock>("ConditionCount")!.Text);
            Assert.False(rows[0].FindControl<Grid>("OrSeparator")!.IsVisible);
            Assert.All(rows.Skip(1), row => Assert.True(row.FindControl<Grid>("OrSeparator")!.IsVisible));

            MenuItem MenuItem(WelcomeCondition kind) => menu.Items.Cast<MenuItem>().Single(item => Equals(item.Tag, kind));
            void Add(WelcomeCondition kind)
            {
                menu.ShowAt(button);
                GuiHarness.Flush();
                MenuItem(kind).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                GuiHarness.Flush();
                Assert.False(menu.IsOpen);
                var row = Rows(page).Last(row => row.Kind == kind);
                Assert.True(row.IsKeyboardFocusWithin);
            }
        });

    [Fact]
    public Task Removing_all_conditions_saves_disabled_values_and_shows_empty_state() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  uids: [42, 43] # 观众\n  fan_club_level: 1 # 粉丝团\n  guard: 舰长 # 大航海\n  wealth_level: 10 # 荣耀\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.Equal(new[] { WelcomeCondition.Uid, WelcomeCondition.Uid, WelcomeCondition.FanClubLevel, WelcomeCondition.Guard, WelcomeCondition.WealthLevel }, Rows(page).Select(row => row.Kind));
            foreach (var row in Rows(page)) Remove(row);
            Assert.True(page.FindControl<Grid>("EmptyConditions")!.IsVisible);
            Assert.False(page.FindControl<TextBlock>("ConditionCount")!.IsVisible);
            Assert.True(page.FindControl<Button>("AddConditionButton")!.IsVisible);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("uids: [42, 43]", "uids: []").Replace("fan_club_level: 1", "fan_club_level: 0")
                .Replace("guard: 舰长", "guard: 关闭").Replace("wealth_level: 10", "wealth_level: 0"), File.ReadAllText(path));
            Assert.Empty(Rows(page));
            Assert.False(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
        });

    [Fact]
    public Task Removing_and_restoring_equivalent_conditions_is_not_a_change() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  uids: [42]\n  fan_club_level: 1\n  guard: 舰长\n  wealth_level: 10\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            foreach (var row in Rows(page)) Remove(row);
            Add(page, WelcomeCondition.WealthLevel);
            Add(page, WelcomeCondition.Guard);
            Add(page, WelcomeCondition.FanClubLevel);
            Add(page, WelcomeCondition.Uid).FindControl<TextBox>("UidInput")!.Text = "0042";
            GuiHarness.Flush();
            Assert.False(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            Assert.Equal(before, File.ReadAllText(path));
        });

    [Fact]
    public Task A_uid_removed_and_added_back_after_the_others_is_not_a_change() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  uids: [42, 43]\n", (page, _) =>
        {
            Remove(Rows(page)[0]);
            Add(page, WelcomeCondition.Uid).FindControl<TextBox>("UidInput")!.Text = "42";
            GuiHarness.Flush();
            Assert.Equal(["43", "42"], UidLabels(page));
            Assert.False(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Duplicate_uids_in_the_file_are_reported_without_making_a_draft() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  uids: [42, 42]\n", (page, path) =>
        {
            Assert.Equal("和上面那条重复了", Rows(page)[1].FindControl<TextBlock>("ErrorMessage")!.Text);
            Assert.False(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            File.WriteAllText(path, "welcome:\n  uids: [42]\n");
            page.RefreshFileStatus(Settings.Load(Path.GetDirectoryName(path)!));
            Assert.Equal(["42"], UidLabels(page));
            Assert.False(Rows(page)[0].FindControl<TextBlock>("ErrorMessage")!.IsVisible);
        });

    [Theory]
    [InlineData("", "填一个 UID，或删掉这条")]
    [InlineData(" ", "填一个 UID，或删掉这条")]
    [InlineData("0", "UID 必须是正整数")]
    [InlineData("-1", "UID 必须是正整数")]
    [InlineData("1.5", "UID 必须是正整数")]
    [InlineData("abc", "UID 必须是正整数")]
    [InlineData("12345, 67890", "UID 必须是正整数")]
    [InlineData("[0]", "UID 必须是正整数")]
    [InlineData("9223372036854775808", "UID 必须是正整数")]
    [InlineData("0042", "和上面那条重复了")]
    public Task Invalid_uid_is_reported_on_its_row_and_can_be_corrected(string value, string message) =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  uids: [42]\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            var originalRow = Row(page, WelcomeCondition.Uid);
            var row = Add(page, WelcomeCondition.Uid);
            row.FindControl<TextBox>("UidInput")!.Text = value;
            GuiHarness.Flush();
            Assert.Equal(message, row.FindControl<TextBlock>("ErrorMessage")!.Text);
            Assert.True(row.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.False(originalRow.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.True(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            var save = page.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
            row.FindControl<TextBox>("UidInput")!.Text = "9223372036854775807";
            GuiHarness.Flush();
            Assert.False(row.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("uids: [42]", "uids: [42, 9223372036854775807]"), File.ReadAllText(path));
        });

    [Fact]
    public Task Removing_the_first_duplicate_clears_the_remaining_rows_error() =>
        WithSettingsPage(SettingsSection.Welcome, null, (page, _) =>
        {
            SetUids(page, "42", "42");
            var rows = Rows(page).Where(row => row.Kind == WelcomeCondition.Uid).ToArray();
            Assert.True(rows[1].FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Remove(rows[0]);
            Assert.False(rows[1].FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.True(page.FindControl<Button>("SaveButton")!.IsEnabled);
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
        WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
        {
            var before = File.ReadAllText(path);
            var row = Add(page, kind);
            var number = row.FindControl<NumericUpDown>("LevelInput")!;
            var input = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            input.Text = value;
            GuiHarness.Flush();
            Assert.Equal("至少 1 级", row.FindControl<TextBlock>("ErrorMessage")!.Text);
            var save = page.FindControl<Button>("SaveButton")!;
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
        WithSettingsPage(SettingsSection.Welcome, "welcome: {uids: [], fan_club_level: 0, guard: 关闭, wealth_level: 0}\n", (page, _) =>
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
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  wealth_level: |-\n    0\n", (page, _) =>
        {
            var hint = page.FindControl<TextBlock>("ConditionsReadOnly")!;
            Assert.True(hint.IsVisible);
            Assert.Equal("荣耀等级是多行写法，请在设置文件里改", hint.Text);
            Assert.Equal(new[] { true, true, false, false },
                Menu(page).Items.Cast<MenuItem>().Select(item => item.IsEnabled));
            Remove(Row(page, WelcomeCondition.Guard));
            Assert.True(Menu(page).Items.Cast<MenuItem>().Single(item => Equals(item.Tag, WelcomeCondition.Guard)).IsEnabled);
            Assert.Equal("荣耀等级是多行写法，请在设置文件里改", hint.Text);
        });

    [Fact]
    public Task A_multiline_threshold_is_read_only_while_other_conditions_can_be_saved() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n  fan_club_level: |-\n    21\n  guard: 舰长\n", (page, path) =>
        {
            var before = File.ReadAllText(path);
            var row = Row(page, WelcomeCondition.FanClubLevel);
            Assert.True(row.FindControl<NumericUpDown>("LevelInput")!.IsReadOnly);
            Assert.False(row.FindControl<NumericUpDown>("LevelInput")!.AllowSpin);
            Assert.False(row.FindControl<Button>("RemoveButton")!.IsVisible);
            Assert.True(row.FindControl<TextBlock>("ReadOnlyMessage")!.IsVisible);
            Assert.False(page.FindControl<TextBlock>("ConditionsReadOnly")!.IsVisible);
            Remove(row);
            Assert.Same(row, Row(page, WelcomeCondition.FanClubLevel));
            Remove(Row(page, WelcomeCondition.Guard));
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("guard: 舰长", "guard: 关闭"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData("欢迎{舰长}")]
    [InlineData("欢迎{@}")]
    [InlineData("欢迎{昵称")]
    [InlineData("欢迎}")]
    public Task Invalid_welcome_placeholders_disable_save_and_can_be_corrected(string text) =>
        WithSettingsPage(SettingsSection.Welcome, null, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = WelcomeTextList.Input(window);
            input.Text = text;
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            Assert.Contains("welcome.texts", WelcomeTextList.Error(window).Text);
            Assert.Contains("可用占位符为 {@}（只能放开头）、{昵称}、{大航海}", WelcomeTextList.Error(window).Text);
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
        WithSettingsPage(SettingsSection.Welcome, null, (window, path) =>
        {
            WelcomeTextList.Input(window).Text = "一二三四五六七八九十百";
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.Equal("一二三四五六七八九十百", settings.WelcomeTexts.Value[0]);
        });

    [Fact]
    public Task Welcome_defaults_and_help_are_shown_and_missing_values_can_be_saved() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.False(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked);
            Assert.Equal("欢迎{大航海}{昵称}", WelcomeTextList.Input(window).Text);
            Assert.Null(window.FindControl<NumericUpDown>("WelcomeMaxLengthInput"));
            Assert.Empty(UidLabels(window));
            Assert.DoesNotContain(Rows(window), row => row.Kind == WelcomeCondition.FanClubLevel);
            Assert.Equal("舰长", Row(window, WelcomeCondition.Guard).FindControl<ComboBox>("GuardInput")!.SelectedItem);
            Assert.DoesNotContain(Rows(window), row => row.Kind == WelcomeCondition.WealthLevel);
            var hint = window.FindControl<TextBlock>("WelcomeTextHint")!.Text;
            Assert.Contains("英文、数字、半角符号算 1，其他字符算 2", hint);
            Assert.Contains("上限自动读取账号的", hint);
            Assert.Contains("{@}（只能放开头）、{昵称}、{大航海}", hint);
            Assert.Contains("{{ 和 }}", hint);
            SetUids(window, "12345", "67890");
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var after = File.ReadAllText(path);
            Assert.Contains("  uids: [12345, 67890]\n", after);
            Assert.Equal(before, after.Replace("  uids: [12345, 67890]\n", ""));
            Assert.Equal([12345L, 67890L], Settings.Load(Path.GetDirectoryName(path)!).WelcomeUids.Value);
        });

    [Fact]
    public Task Flow_welcome_group_is_read_only_and_preserved_when_other_settings_are_saved() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome: {enabled: true, texts: ['欢迎{昵称}'], uids: [12345, 67890], fan_club_level: 21, guard: 提督, wealth_level: 45}\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked);
            Assert.False(window.FindControl<ToggleSwitch>("WelcomeInput")!.IsEnabled);
            Assert.Equal("欢迎{昵称}", WelcomeTextList.Input(window).Text);
            Assert.True(WelcomeTextList.Input(window).IsReadOnly);
            Assert.Equal(["12345", "67890"], UidLabels(window));
            Assert.All(Rows(window).Where(row => row.Kind == WelcomeCondition.Uid), row => Assert.True(row.FindControl<TextBox>("UidInput")!.IsReadOnly));
            Assert.Equal("提督", Row(window, WelcomeCondition.Guard).FindControl<ComboBox>("GuardInput")!.SelectedItem);
            Assert.False(Row(window, WelcomeCondition.Guard).FindControl<ComboBox>("GuardInput")!.IsEnabled);
            foreach (var (name, value) in new[] { (WelcomeCondition.FanClubLevel, "21"), (WelcomeCondition.WealthLevel, "45") })
            {
                var number = Row(window, name).FindControl<NumericUpDown>("LevelInput")!;
                Assert.Equal(value, number.Text);
                Assert.True(number.IsReadOnly);
                Assert.False(number.AllowSpin);
            }
            foreach (var name in new[] { "Welcome", "WelcomeText" })
                Assert.True(window.FindControl<TextBlock>(name + "ReadOnly")!.IsVisible);
            Assert.All(Rows(window), row =>
            {
                Assert.True(row.FindControl<TextBlock>("ReadOnlyMessage")!.IsVisible);
                Assert.False(row.FindControl<Button>("RemoveButton")!.IsVisible);
            });
            Assert.All(Menu(window).Items.Cast<MenuItem>(), item => Assert.False(item.IsEnabled));
            var general = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.General);
            general.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            Dispatcher.UIThread.RunJobs();
            var save = general.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Information", "level: Warning"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData(null)]
    [InlineData("  announce: true # 开关\n")]
    public Task Announcement_switch_defaults_on_and_saves_only_its_line(string? line) =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  enabled: true\n" + line, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("AnnounceInput");
            Assert.NotNull(input);
            Assert.True(input.IsChecked);
            Assert.True(input.IsEnabled);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
            input.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(line is null ? before.Replace("  enabled: true\n", "  enabled: true\n  announce: false\n")
                : before.Replace("announce: true", "announce: false"), File.ReadAllText(path));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxAnnounce.Value);
        });

    [Fact]
    public Task Multiline_announcement_switch_is_read_only_and_preserved() =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  announce: |-\n    false\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("AnnounceInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("AnnounceReadOnly")!.IsVisible);
            window.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true", "enabled: false"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_tally_and_send_queue_fields_load_defaults_validate_and_save_missing_settings() =>
        WithSettingsPage(SettingsSection.BlindBox, "# 保留\nblind_box:\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("DailyKeywordInput");
            var monthly = window.FindControl<TextBox>("MonthlyKeywordInput");
            var send = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.Send);
            var interval = send.FindControl<NumericUpDown>("SendIntervalInput");
            Assert.NotNull(keyword);
            Assert.NotNull(monthly);
            Assert.NotNull(interval);
            Assert.Equal("今日盲盒", keyword.Text);
            Assert.Equal("本月盲盒", monthly.Text);
            Assert.Equal("1", interval.Text);
            var save = window.FindControl<Button>("SaveButton")!;
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
            save = send.FindControl<Button>("SaveButton")!;
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
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  daily_keyword: |-\n    查盲盒\n  monthly_keyword: |-\n    查月盲盒\nsend:\n  interval_seconds: |-\n    7\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("DailyKeywordInput");
            var interval = window.FindControl<NumericUpDown>("SendIntervalInput");
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
            Assert.True(window.FindControl<TextBlock>("SendIntervalReadOnly")!.IsVisible);
            window.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("\nsend:", "\n  enabled: false\nsend:"), File.ReadAllText(path));
        });

    [Fact]
    public Task Multiline_blind_box_switch_is_read_only_and_preserved_when_saving() =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  enabled: |-\n    false\nlog:\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("BlindBoxInput")!;
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("BlindBoxReadOnly")!.IsVisible);
            var general = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.General);
            general.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            general.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Debug", "level: Warning"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_switch_loads_changes_and_saves_without_touching_other_lines() =>
        WithSettingsPage(SettingsSection.BlindBox, "# 保留\nblind_box:\n  enabled: false # 开关\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<ToggleSwitch>("BlindBoxInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            input.IsChecked = true;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: false # 开关", "enabled: true # 开关"), File.ReadAllText(path));
            Assert.True(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
            Assert.StartsWith("已保存", window.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
            input.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
        });

    [Fact]
    public Task Multiline_level_is_read_only() =>
        WithSettingsPage(SettingsSection.General, "log:\n  level: |-\n    Debug\n", (window, _) =>
        {
            Assert.False(window.FindControl<ComboBox>("LogLevelInput")!.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("LogLevelReadOnly")!.IsVisible);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Save_rereads_external_changes_and_refreshes_the_form() =>
        WithSettingsPage(SettingsSection.General, null, (window, path) =>
        {
            File.WriteAllText(path, "# 外部修改\nlog:\n  retention_days: 30\n  level: Warning\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "7";
            var save = window.FindControl<Button>("SaveButton")!;
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
            Assert.Contains("第 2 行", window.FindControl<TextBlock>("FileMessage")!.Text);
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
        WithSettingsPage(SettingsSection.General, null, (window, _) =>
        {
            var number = window.FindControl<NumericUpDown>("RetentionInput")!;
            var textBox = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            textBox.Text = input;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Settings.Validate("log.retention_days", input)!, window.FindControl<TextBlock>("RetentionError")!.Text);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
            textBox.Text = "14";
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Theory]
    [InlineData("log:\n  level: @bad\n", "第 2 行")]
    [InlineData("log:\n  retention_days: 0\n", "log.retention_days")]
    [InlineData("log:\n  !!str level: Debug\n  level: Warning\n", "第 3 行（log.level）：设置名重复")]
    [InlineData("!!str log:\n  level: Debug\nlog:\n  retention_days: 7\n", "第 3 行（log）：分组名重复")]
    public Task File_errors_display_core_location_and_disable_save(string yaml, string expected) =>
        WithSettingsPage(SettingsSection.General, yaml, (window, path) =>
        {
            var message = window.FindControl<TextBlock>("FileMessage")!;
            Assert.True(message.IsVisible);
            Assert.Contains(path, message.Text);
            Assert.Contains(expected, message.Text);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Multiline_settings_are_read_only_and_do_not_block_editing_other_settings() =>
        WithSettingsPage(SettingsSection.General, "log:\n  retention_days: |-\n    7\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<NumericUpDown>("RetentionInput")!.IsReadOnly);
            Assert.False(window.FindControl<NumericUpDown>("RetentionInput")!.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("RetentionReadOnly")!.IsVisible);
            window.FindControl<ComboBox>("LogLevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                main.FindControl<ListBox>("Navigation")!.SelectedIndex = 5;
                GuiHarness.Flush();
                var window = main.GetVisualDescendants().OfType<SettingsPage>().Single(page => page.IsEffectivelyVisible);
                Assert.Empty(main.OwnedWindows);
                var before = File.ReadAllText(path);
                Assert.Contains("原样保留", window.FindControl<TextBlock>("UnknownKeysMessage")!.Text);
                window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
                Dispatcher.UIThread.RunJobs();
                var save = window.FindControl<Button>("SaveButton")!;
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
        .Where(row => row.Kind == WelcomeCondition.Uid).Select(row => row.FindControl<TextBox>("UidInput")!.Text);

    private static void SetUids(SettingsPage page, params string[] values)
    {
        foreach (var row in Rows(page).Where(row => row.Kind == WelcomeCondition.Uid)) Remove(row);
        foreach (var value in values) Add(page, WelcomeCondition.Uid).FindControl<TextBox>("UidInput")!.Text = value;
        GuiHarness.Flush();
    }

    [Fact]
    public Task Welcome_insert_buttons_use_caret_and_at_viewer_is_inserted_only_once() => WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
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
    public Task Escaped_at_text_does_not_block_inserting_an_at_placeholder() => WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
    {
        var text = WelcomeTextList.Input(page);
        text.Text = "欢迎{{@}}";
        var button = page.FindControl<Button>("InsertAtButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("{@}欢迎{{@}}", text.Text);
        Assert.Null(Settings.Validate("welcome.texts", Settings.TextListText([text.Text!])));
    });

    private static Task WithSettingsPage(SettingsSection section, string? yaml, Action<SettingsPage, string> test) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var path = Settings.FilePath(harness.DataDirectory);
            if (yaml is not null)
            {
                Directory.CreateDirectory(harness.DataDirectory);
                File.WriteAllText(path, yaml);
            }
            var page = new SettingsPage(harness.DataDirectory, section);
            var window = new Window { Content = page, Width = 736, Height = 640 };
            window.Show();
            try { test(page, path); }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
}
