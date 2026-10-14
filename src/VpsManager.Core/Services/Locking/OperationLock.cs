using System;
using System.Threading;

namespace VpsManager.Core.Services.Locking;

public sealed class OperationLock : IOperationLock
{
    private readonly object _sync = new();
    private string? _activeOperationDescription;
    private DateTime? _lockedAt;
    private int _isLocked;

    public bool IsLocked => Volatile.Read(ref _isLocked) == 1;
    public string? ActiveOperationDescription => _activeOperationDescription;
    public DateTime? LockedAt => _lockedAt;

    public event EventHandler<OperationLockChangedEventArgs>? LockChanged;

    public IDisposable AcquireLock(string operationDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationDescription);

        lock (_sync)
        {
            if (IsLocked)
            {
                throw new InvalidOperationException(
                    $"Cannot perform '{operationDescription}' because '{_activeOperationDescription}' is currently in progress. Please wait for it to complete.");
            }

            _activeOperationDescription = operationDescription;
            _lockedAt = DateTime.UtcNow;
            Volatile.Write(ref _isLocked, 1);
        }

        OnLockChanged(true, operationDescription);
        return new Releaser(this);
    }

    public bool TryAcquireLock(string operationDescription, out IDisposable? lockHandle)
    {
        lock (_sync)
        {
            if (IsLocked)
            {
                lockHandle = null;
                return false;
            }

            _activeOperationDescription = operationDescription;
            _lockedAt = DateTime.UtcNow;
            Volatile.Write(ref _isLocked, 1);
        }

        OnLockChanged(true, operationDescription);
        lockHandle = new Releaser(this);
        return true;
    }

    private void Release()
    {
        string? priorOp;
        lock (_sync)
        {
            if (!IsLocked) return;

            priorOp = _activeOperationDescription;
            _activeOperationDescription = null;
            _lockedAt = null;
            Volatile.Write(ref _isLocked, 0);
        }

        OnLockChanged(false, null);
    }

    private void OnLockChanged(bool isLocked, string? desc)
    {
        LockChanged?.Invoke(this, new OperationLockChangedEventArgs(isLocked, desc));
    }

    private sealed class Releaser : IDisposable
    {
        private OperationLock? _owner;

        public Releaser(OperationLock owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
