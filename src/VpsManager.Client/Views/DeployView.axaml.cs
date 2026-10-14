using System;
using System.ComponentModel;
using Avalonia.Controls;

namespace VpsManager.Client.Views;

public partial class DeployView : UserControl
{
    public DeployView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.DeployViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.DeployViewModel.TerminalOutput))
        {
            var scrollViewer = this.FindControl<ScrollViewer>("DeployLogScrollViewer");
            scrollViewer?.ScrollToEnd();
        }
    }
}
