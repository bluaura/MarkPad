using MarkPad.Core.Documents;

namespace MarkPad.App.Services;

public enum DiscardChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Window-bound UI the ViewModels need (pickers, confirmations, notifications).</summary>
public interface IDialogService
{
    Task<IReadOnlyList<string>> PickOpenFilesAsync();

    Task<string?> PickSavePathAsync(string suggestedName, DocumentKind kind, string? initialDirectory);

    Task<DiscardChoice> ConfirmDiscardAsync(string fileName);

    void ShowInfo(string message, bool isError = false);
}
