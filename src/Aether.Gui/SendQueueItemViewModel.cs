using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class SendQueueItemViewModel(SendQueueUpdate first) : ObservableObject
{
    private SendQueueUpdate latest = first;
    private bool connectionEnded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private int position;

    public long Id { get; } = first.Id;
    public SendQueueItemKind Kind { get; } = first.Kind;
    public string Nickname { get; } = first.Nickname;
    public DateTimeOffset OccurredAt { get; } = first.OccurredAt;
    public string KindText => Kind switch
    {
        SendQueueItemKind.Welcome => "进场欢迎",
        SendQueueItemKind.BlindBoxQuery => "盲盒查询",
        _ => "盲盒播报"
    };
    public string Detail { get; } = first.Kind switch
    {
        SendQueueItemKind.BlindBoxQuery => "查询",
        SendQueueItemKind.BlindBoxAnnouncement => "播报",
        _ => first.WelcomeCondition switch
        {
            WelcomeCondition.Uid => "指定 UID",
            WelcomeCondition.FanClubLevel => $"粉丝团 {first.WelcomeValue}",
            WelcomeCondition.Guard => first.WelcomeValue ?? "",
            WelcomeCondition.WealthLevel => $"荣耀 {first.WelcomeValue}",
            _ => ""
        }
    };
    /// <summary>Welcomes have it from the start; queries and announcements from their first Sending on.</summary>
    public string? Text { get; private set; } = first.Text;
    public string TextOrPlaceholder => Text ?? "发送时计算金额";
    public bool IsSent => latest.State == SendQueueItemState.Sent;
    public bool IsFinished => connectionEnded || latest.State is SendQueueItemState.Sent or SendQueueItemState.Discarded or SendQueueItemState.NotQueued;
    public bool IsUnsent => IsFinished && !IsSent;
    public string Status => connectionEnded ? "连接结束，没发出" : latest.State switch
    {
        SendQueueItemState.Queued => $"排队中 · 第 {Position} 条",
        SendQueueItemState.Sending => "发送中",
        SendQueueItemState.Sent => $"已发送 {latest.OccurredAt:HH:mm:ss}",
        SendQueueItemState.WaitingToRetry => FormattableString.Invariant($"被限流，{latest.RetrySeconds} 秒后重试"),
        _ => $"没发出：{latest.Reason}"
    };

    public void Apply(SendQueueUpdate update)
    {
        latest = update;
        Text = update.Text ?? Text;
        OnPropertyChanged(string.Empty);
    }

    public void EndConnection()
    {
        connectionEnded = true;
        OnPropertyChanged(string.Empty);
    }
}
