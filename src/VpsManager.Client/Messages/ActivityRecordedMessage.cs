using System;

namespace VpsManager.Client.Messages;

public enum ActivityKind
{
    Deployment,
    Backup,
    General
}

/// <summary>
/// Cross-tab notification message published whenever a major deployment, backup, or lifecycle operation finishes.
/// Subscribed to by ActivityFeedViewModel without tight coupling between ViewModels.
/// </summary>
public sealed record ActivityRecordedMessage(ActivityKind Kind, string Summary, DateTime Timestamp);
