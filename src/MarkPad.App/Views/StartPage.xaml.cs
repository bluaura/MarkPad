using MarkPad.App.ViewModels;
using MarkPad.Core.Mru;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Views;

/// <summary>Empty state with recent files (PRD F-FILE-04 "시작 화면").</summary>
public sealed partial class StartPage : UserControl
{
    public StartPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.RecentFiles)) Bindings.Update();
        };
    }

    public ShellViewModel ViewModel { get; }

    public bool HasNoRecent => ViewModel.RecentFiles.Count == 0;

    private async void OnRecentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentFile file)
        {
            await ViewModel.OpenRecentCommand.ExecuteAsync(file);
        }
    }
}
