using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsManager.Core.Models;

public enum FileChangeStatus
{
    Unchanged,
    Modified,
    New,
    Deleted
}

public class FilePatchNode : ObservableObject
{
    private bool _isSelected;
    private bool _isVisibleInFilter = true;
    private FileChangeStatus _changeStatus = FileChangeStatus.Unchanged;

    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }

    public FilePatchNode? Parent { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                // Cascade selection to children
                foreach (var child in Children)
                {
                    child.SetIsSelectedInternal(value);
                }

                // Notify parent to re-evaluate its selection
                Parent?.EvaluateParentSelection();
            }
        }
    }

    internal void SetIsSelectedInternal(bool value)
    {
        if (SetProperty(ref _isSelected, value, nameof(IsSelected)))
        {
            foreach (var child in Children)
            {
                child.SetIsSelectedInternal(value);
            }
        }
    }

    private void EvaluateParentSelection()
    {
        if (Children.Count == 0) return;

        bool allSelected = Children.All(c => c.IsSelected);
        if (_isSelected != allSelected)
        {
            _isSelected = allSelected;
            OnPropertyChanged(nameof(IsSelected));
            Parent?.EvaluateParentSelection();
        }
    }

    public FileChangeStatus ChangeStatus
    {
        get => _changeStatus;
        set
        {
            if (SetProperty(ref _changeStatus, value))
            {
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(IsDeleted));
                OnPropertyChanged(nameof(IsUnchanged));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public bool IsModified => ChangeStatus == FileChangeStatus.Modified;
    public bool IsNew => ChangeStatus == FileChangeStatus.New;
    public bool IsDeleted => ChangeStatus == FileChangeStatus.Deleted;
    public bool IsUnchanged => ChangeStatus == FileChangeStatus.Unchanged;

    public bool IsVisibleInFilter
    {
        get => _isVisibleInFilter;
        set => SetProperty(ref _isVisibleInFilter, value);
    }

    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }

    public string FormattedSize => FormatSize(FileSizeBytes, IsDirectory);

    public static string FormatSize(long bytes, bool isDirectory = false)
    {
        if (isDirectory && bytes <= 0) return string.Empty;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public string StatusBadgeText => ChangeStatus switch
    {
        FileChangeStatus.Modified => "MODIFIED",
        FileChangeStatus.New => "NEW",
        FileChangeStatus.Deleted => "DELETED",
        _ => "UNCHANGED"
    };

    public ObservableCollection<FilePatchNode> Children { get; } = new();

    public void AddChild(FilePatchNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public long GetTotalSizeBytes()
    {
        if (!IsDirectory) return FileSizeBytes;
        return Children.Sum(c => c.GetTotalSizeBytes());
    }

    public long GetSelectedSizeBytes()
    {
        if (!IsDirectory) return IsSelected ? FileSizeBytes : 0;
        return Children.Sum(c => c.GetSelectedSizeBytes());
    }
}

public class PatchDeploymentOptions
{
    public bool CreateBackupSnapshot { get; set; } = true;
    public bool AutoReloadService { get; set; } = true;
    public bool IsDockerized { get; set; }
    public string ComposeFileName { get; set; } = "docker-compose.yml";
    public string ServiceName { get; set; } = string.Empty;
    public string RestartCommand { get; set; } = string.Empty;
    public string LaunchCommand { get; set; } = string.Empty;
    public ProjectType ProjectType { get; set; } = ProjectType.DockerCompose;
}

public class PatchDeploymentResult
{
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int FilesDeployedCount { get; set; }
    public int FilesDeletedCount { get; set; }
    public long PayloadSizeBytes { get; set; }
    public string? BackupPath { get; set; }
    public TimeSpan Duration { get; set; }
}

public class GitWorkingTreeStatus
{
    public bool IsGitRepository { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool HasUncommittedChanges { get; set; }
    public int ModifiedCount { get; set; }
    public int NewCount { get; set; }
    public int DeletedCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}
