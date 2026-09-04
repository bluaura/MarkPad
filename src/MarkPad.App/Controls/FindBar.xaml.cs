using MarkPad.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace MarkPad.App.Controls;

/// <summary>Find/replace bar (PRD F-EDIT-11): Enter = next, Shift+Enter = previous, Esc = close.</summary>
public sealed partial class FindBar : UserControl
{
    private FindViewModel _viewModel = new(() => null);

    public FindBar()
    {
        InitializeComponent();
    }

    public FindViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel = value;
            _viewModel.FocusRequested += OnFocusRequested;
            Bindings.Update();
        }
    }

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            QueryBox.Focus(FocusState.Programmatic);
            QueryBox.SelectAll();
        });
    }

    private static bool ShiftDown()
        => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (ShiftDown()) ViewModel.PreviousCommand.Execute(null);
                else ViewModel.NextCommand.Execute(null);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CloseCommand.Execute(null);
                break;
        }
    }

    private void OnReplaceKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                ViewModel.ReplaceCommand.Execute(null);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CloseCommand.Execute(null);
                break;
        }
    }
}
