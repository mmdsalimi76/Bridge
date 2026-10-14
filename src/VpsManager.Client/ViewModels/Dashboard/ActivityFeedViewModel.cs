using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using VpsManager.Client.Messages;

namespace VpsManager.Client.ViewModels.Dashboard;

/// <summary>
/// A short rolling log of what happened in this session, fed by the deploy and database tabs via WeakReferenceMessenger.
/// </summary>
public partial class ActivityFeedViewModel : ViewModelBase
{
    private const int MaxEntries = 8;

    [ObservableProperty]
    private bool _hasEntries;

    public ActivityFeedViewModel()
    {
        Entries = new ObservableCollection<string>();
        Entries.CollectionChanged += (_, _) => HasEntries = Entries.Count > 0;

        WeakReferenceMessenger.Default.Register<ActivityRecordedMessage>(this, (_, msg) =>
        {
            switch (msg.Kind)
            {
                case ActivityKind.Deployment:
                    RecordDeployment(msg.Summary, msg.Timestamp);
                    break;
                case ActivityKind.Backup:
                    RecordBackup(msg.Summary, msg.Timestamp);
                    break;
                default:
                    Add(msg.Summary, msg.Timestamp);
                    break;
            }
        });
    }

    public ObservableCollection<string> Entries { get; }

    public void Add(string message, DateTime? occurredAt = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Entries.Insert(0, $"{(occurredAt ?? DateTime.Now).ToLocalTime():HH:mm:ss}  {message}");
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    public void RecordDeployment(string summary, DateTime completedAt) =>
        Add($"Deployment: {summary}", completedAt);

    public void RecordBackup(string summary, DateTime completedAt) =>
        Add($"Backup: {summary}", completedAt);

    public void Clear() => Entries.Clear();
}
