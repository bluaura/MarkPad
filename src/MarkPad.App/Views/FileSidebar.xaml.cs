using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Views;

public sealed partial class FileSidebar : UserControl
{
    private FileSidebarViewModel _viewModel = new();

    public FileSidebar()
    {
        InitializeComponent();
    }

    public FileSidebarViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel.Roots.CollectionChanged -= OnRootsChanged;
            _viewModel = value;
            _viewModel.Roots.CollectionChanged += OnRootsChanged;
            Bindings.Update();
        }
    }

    public bool IsEmpty => ViewModel.Roots.Count == 0;

    private void OnRootsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Bindings.Update();

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        Bindings.Update();
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FileEntry entry)
        {
            if (entry.IsDirectory) entry.IsExpanded = !entry.IsExpanded;
            else ViewModel.RequestOpen(entry);
        }
    }
}
