using System;

namespace VpsManager.Core.Services.Locking;

public class OperationLockChangedEventArgs : EventArgs
{
    public bool IsLocked { get; }
    public string? OperationDescription { get; }

    public OperationLockChangedEventArgs(bool isLocked, string? operationDescription)
    {
        IsLocked = isLocked;
        OperationDescription = operationDescription;
    }
}

public interface IOperationLock
{
    bool IsLocked { get; }
    string? ActiveOperationDescription { get; }
    DateTime? LockedAt { get; }

    /// <summary>
    /// Acquires exclusive operation lock. Returns a disposable handle that releases the lock on disposal.
    /// Throws InvalidOperationException if an operation is already in progress.
    /// </summary>
    IDisposable AcquireLock(string operationDescription);

    /// <summary>
    /// Attempts to acquire exclusive operation lock without throwing.
    /// </summary>
    bool TryAcquireLock(string operationDescription, out IDisposable? lockHandle);

    event EventHandler<OperationLockChangedEventArgs>? LockChanged;
}
