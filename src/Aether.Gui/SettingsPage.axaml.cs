using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Aether.Gui;

public abstract class SettingsPage : TemplatedControl
{
    protected readonly string dataDirectory;
    protected Settings settings = null!;
    protected MainWindowViewModel? viewModel;
    protected Dictionary<string, string?> original = [];
    private readonly List<(Action Load, Func<Field> Read)> bindings = [];
    private bool loading;
    private bool saving;
    private TaskCompletionSource? saveCompletion;
    private Button? saveButton;
    private Border? recentActivity;
    private Grid? pageLayout;
    internal Task PendingSave => saveCompletion?.Task ?? Task.CompletedTask;
    /// <summary>Raised once Save has written the file or failed to, so other views can reread it.</summary>
    public event Action? SaveCompleted;

    public static readonly StyledProperty<Control?> HeaderContentProperty =
        AvaloniaProperty.Register<SettingsPage, Control?>(nameof(HeaderContent), null);
    public Control? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }

    public static readonly StyledProperty<Control?> HeaderNoteProperty =
        AvaloniaProperty.Register<SettingsPage, Control?>(nameof(HeaderNote), null);
    public Control? HeaderNote { get => GetValue(HeaderNoteProperty); set => SetValue(HeaderNoteProperty, value); }

    public static readonly StyledProperty<Control?> BodyContentProperty =
        AvaloniaProperty.Register<SettingsPage, Control?>(nameof(BodyContent), null);
    public Control? BodyContent { get => GetValue(BodyContentProperty); set => SetValue(BodyContentProperty, value); }

    public static readonly StyledProperty<Control?> ActivityContentProperty =
        AvaloniaProperty.Register<SettingsPage, Control?>(nameof(ActivityContent), null);
    public Control? ActivityContent { get => GetValue(ActivityContentProperty); set => SetValue(ActivityContentProperty, value); }

    public static readonly StyledProperty<Control?> BelowBodyContentProperty =
        AvaloniaProperty.Register<SettingsPage, Control?>(nameof(BelowBodyContent), null);
    public Control? BelowBodyContent { get => GetValue(BelowBodyContentProperty); set => SetValue(BelowBodyContentProperty, value); }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(Title), "");
    public string Title { get => GetValue(TitleProperty); protected set => SetValue(TitleProperty, value); }

    public static readonly StyledProperty<string> TimingHintProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(TimingHint), "下次连接直播间时生效");
    public string TimingHint { get => GetValue(TimingHintProperty); protected set => SetValue(TimingHintProperty, value); }

    public static readonly StyledProperty<SettingsException?> FileErrorProperty =
        AvaloniaProperty.Register<SettingsPage, SettingsException?>(nameof(FileError), null);
    public SettingsException? FileError { get => GetValue(FileErrorProperty); protected set => SetValue(FileErrorProperty, value); }

    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<SettingsPage, bool>(nameof(IsDirty), false);
    public bool IsDirty { get => GetValue(IsDirtyProperty); protected set => SetValue(IsDirtyProperty, value); }

    public static readonly StyledProperty<string> DirtyTextProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(DirtyText), "有未保存的修改");
    public string DirtyText { get => GetValue(DirtyTextProperty); protected set => SetValue(DirtyTextProperty, value); }

    public static readonly StyledProperty<bool> CanSaveProperty =
        AvaloniaProperty.Register<SettingsPage, bool>(nameof(CanSave), false);
    public bool CanSave { get => GetValue(CanSaveProperty); protected set => SetValue(CanSaveProperty, value); }

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(Message), "");
    public string Message { get => GetValue(MessageProperty); protected set => SetValue(MessageProperty, value); }

    public static readonly StyledProperty<bool> ShowReconnectNoticeProperty =
        AvaloniaProperty.Register<SettingsPage, bool>(nameof(ShowReconnectNotice), false);
    public bool ShowReconnectNotice { get => GetValue(ShowReconnectNoticeProperty); protected set => SetValue(ShowReconnectNoticeProperty, value); }

    public static readonly StyledProperty<bool> HasUnknownKeysProperty =
        AvaloniaProperty.Register<SettingsPage, bool>(nameof(HasUnknownKeys), false);
    public bool HasUnknownKeys { get => GetValue(HasUnknownKeysProperty); protected set => SetValue(HasUnknownKeysProperty, value); }

    public static readonly StyledProperty<string> UnknownKeysTextProperty =
        AvaloniaProperty.Register<SettingsPage, string>(nameof(UnknownKeysText), "");
    public string UnknownKeysText { get => GetValue(UnknownKeysTextProperty); protected set => SetValue(UnknownKeysTextProperty, value); }

    protected SettingsPage(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        SizeChanged += (_, _) => ArrangeActivity();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (saveButton is not null) saveButton.Click -= Save;
        base.OnApplyTemplate(e);
        saveButton = e.NameScope.Find<Button>("PART_SaveButton");
        if (saveButton is not null) saveButton.Click += Save;
        recentActivity = e.NameScope.Find<Border>("PART_RecentActivity");
        pageLayout = e.NameScope.Find<Grid>("PART_PageLayout");
        if (e.NameScope.Find<Grid>("PART_FieldsAndSendActivity") is { } fields)
        {
            fields.RowDefinitions[0].Height = BelowBodyContent is null ? GridLength.Star : GridLength.Auto;
            fields.RowDefinitions[1].Height = BelowBodyContent is null ? new GridLength(0) : GridLength.Star;
        }
        ArrangeActivity();
    }

    private void ArrangeActivity()
    {
        if (recentActivity is not { IsVisible: true } || pageLayout is null) return;
        var stacked = Bounds.Width < 680;
        Grid.SetColumn(recentActivity, stacked ? 0 : 1);
        Grid.SetRow(recentActivity, stacked ? 1 : 0);
        recentActivity.Width = stacked ? double.NaN : 300;
        recentActivity.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        pageLayout.RowDefinitions[1].Height = stacked ? GridLength.Star : new GridLength(0);
    }

    protected virtual bool ReconnectAfterSave => true;
    protected virtual bool KeepMessage => false;
    protected virtual string DirtyDescription => "有未保存的修改";
    protected virtual bool RowsChanged => false;
    protected virtual void LoadRows() { }
    protected virtual IEnumerable<Field> RowFields() => [];
    protected virtual bool ShowRowErrors() => false;

    protected void LoadSettings(Settings? current = null)
    {
        loading = true;
        settings = current ?? Settings.Load(dataDirectory);
        ShowFileStatus(settings);
        foreach (var binding in bindings) binding.Load();
        LoadRows();
        original = Fields().ToDictionary(field => field.Name, field => field.Input);
        loading = false;
        Validate();
    }

    /// <summary>Rereads an untouched page; a draft keeps its inputs and only takes the file's status.</summary>
    public void RefreshFileStatus(Settings current)
    {
        if (saving) return;
        // A snapshot with an error filled the inputs with values that must not be used, so a draft over it is dropped.
        if (settings.Error is not null || !IsDirty)
        {
            LoadSettings(current);
            return;
        }
        ShowFileStatus(current);
        Validate();
    }

    protected void ShowFileStatus(Settings current)
    {
        FileError = current.Error;
        HasUnknownKeys = current.UnknownKeys.Count > 0;
        UnknownKeysText = $"未知设置键：{string.Join("、", current.UnknownKeys)}。保存后会原样保留。";
    }

    protected void Bind(NumericUpDown input, TextBlock readOnly, TextBlock? error, Func<Settings, Setting<int>> select)
    {
        Register(setting => Show(input, readOnly, setting), () => input.Text, error, select);
        input.PropertyChanged += (_, e) => { if (e.Property == NumericUpDown.TextProperty) Validate(); };
    }

    protected void Bind(TextBox input, TextBlock readOnly, TextBlock? error, Func<Settings, Setting<string>> select)
    {
        Register(setting => Show(input, readOnly, setting), () => input.Text, error, select);
        input.TextChanged += (_, _) => Validate();
    }

    protected void Bind<T>(ComboBox input, TextBlock readOnly, TextBlock? error, Func<Settings, Setting<T>> select)
    {
        Register(setting => Show(input, readOnly, setting), () => input.SelectedItem?.ToString(), error, select);
        input.SelectionChanged += (_, _) => Validate();
    }

    protected void Bind(ToggleSwitch input, TextBlock readOnly, TextBlock? error, Func<Settings, Setting<bool>> select)
    {
        Register(setting => Show(input, readOnly, setting), () => Settings.BooleanText(input.IsChecked == true), error, select);
        input.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Validate(); };
    }

    private void Register<T>(Action<Setting<T>> show, Func<string?> input, TextBlock? error, Func<Settings, Setting<T>> select) =>
        bindings.Add((() => show(select(settings)), () =>
        {
            var setting = select(settings);
            return new Field(setting.Name, setting.CanEdit, input(), error);
        }));

    // Each control shows its setting; a line that cannot be edited safely leaves it read-only beside its hint.
    private static void Show(NumericUpDown input, TextBlock readOnly, Setting<int> setting)
    {
        input.Value = setting.Value;
        input.Text = setting.Value.ToString(CultureInfo.InvariantCulture);
        input.IsReadOnly = !setting.CanEdit;
        input.AllowSpin = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show(TextBox input, TextBlock readOnly, Setting<string> setting)
    {
        input.Text = setting.Value;
        input.IsReadOnly = !setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show<T>(ComboBox input, TextBlock readOnly, Setting<T> setting)
    {
        input.SelectedItem = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    private static void Show(ToggleSwitch input, TextBlock readOnly, Setting<bool> setting)
    {
        input.IsChecked = setting.Value;
        input.IsEnabled = setting.CanEdit;
        readOnly.IsVisible = !setting.CanEdit;
    }

    protected readonly record struct Field(string Name, bool CanEdit, string? Input, TextBlock? Error = null);

    /// <summary>Each setting this page edits; reading, validating and saving share this list.</summary>
    private Field[] Fields() => bindings.Select(binding => binding.Read()).Concat(RowFields()).ToArray();
    internal virtual IEnumerable<string> FieldNames => Fields().Select(item => item.Name);

    protected void Validate()
    {
        if (loading || saving || settings is null) return;
        var fields = Fields();
        var error = fields.Select(InputError).FirstOrDefault(reason => reason is not null);
        foreach (var field in fields)
        {
            if (field.Error is not { } label) continue;
            label.Text = InputError(field) ?? "";
            label.IsVisible = label.Text.Length > 0;
        }
        var invalidRows = ShowRowErrors();
        IsDirty = RowsChanged || fields.Any(field => field.CanEdit && field.Input != original.GetValueOrDefault(field.Name));
        DirtyText = DirtyDescription;
        CanSave = FileError is null && error is null && !invalidRows && IsDirty;
        if (!KeepMessage || IsDirty) Message = "";
    }

    /// <summary>Core's reason the input cannot be saved, labelled with the setting; read-only settings are never saved.</summary>
    private static string? InputError(Field field) =>
        field.CanEdit && Settings.Validate(field.Name, field.Input) is { } reason ? $"{field.Name}：{reason}" : null;

    /// <summary>The inputs Save would write; read-only settings keep the snapshot's values.</summary>
    private static Dictionary<string, string> Changes(IEnumerable<Field> fields) =>
        fields.Where(field => field.CanEdit).ToDictionary(field => field.Name, field => field.Input!);

    protected virtual Task<string> WriteSettings(Dictionary<string, string> changes)
    {
        Settings.Save(dataDirectory, changes, settings);
        return Task.FromResult("已保存。");
    }

    protected virtual Task<string> AfterSave(Settings saved, string result) => Task.FromResult(result);
    protected virtual void ReloadAfterFailure() => LoadSettings();

    private async void Save(object? sender, RoutedEventArgs e)
    {
        Validate();
        if (!CanSave) return;
        // Core leaves alone what this form was filled with, so an untouched setting keeps its spelling and any edit made elsewhere.
        var changes = Changes(Fields());
        saveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        saving = true;
        IsEnabled = false;
        try
        {
            var result = await WriteSettings(changes);
            // Let LoadSettings validate the saved form, then keep file refreshes out until the save ends: they would clear the result message.
            saving = false;
            LoadSettings();
            saving = true;
            if (settings.Error is null)
            {
                result = await AfterSave(settings, result);
                SaveCompleted?.Invoke();
                Message = result;
                ShowReconnectNotice = ReconnectAfterSave && viewModel?.HasRoomConnection == true;
            }
        }
        catch (Exception error) when (error is SettingsException or IOException or UnauthorizedAccessException)
        {
            saving = false;
            ReloadAfterFailure();
            SaveCompleted?.Invoke();
            Message = error.Message;
        }
        finally { saving = false; IsEnabled = true; saveCompletion?.TrySetResult(); }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= OnRoomStateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += OnRoomStateChanged;
    }

    // The room connection the notice warned about has ended (disconnected, reconnected or failed).
    private void OnRoomStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasRoomConnection) && viewModel?.HasRoomConnection != true)
            ShowReconnectNotice = false;
    }
}
