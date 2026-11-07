namespace Aether.Gui;

internal static class UiTasks
{
    /// <summary>A Core command the window does not wait for, set off by a click or a binding: what it throws is raised on the UI
    /// thread, where the window logs what it does not handle, as it did when the call was synchronous.</summary>
    public static async void RaiseFailure(this Task command) => await command;
}
