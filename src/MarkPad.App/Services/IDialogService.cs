using MarkPad.Core.Documents;

namespace MarkPad.App.Services;

public enum DiscardChoice
{
    Save,
    Discard,
    Cancel,
}

public enum ImageInsertMode
{
    Copy,
    Reference,
    Cancel,
}

/// <summary>Window-bound UI the ViewModels need (pickers, confirmations, notifications).</summary>
public interface IDialogService
{
    Task<IReadOnlyList<string>> PickOpenFilesAsync();

    Task<string?> PickSavePathAsync(string suggestedName, DocumentKind kind, string? initialDirectory);

    /// <summary>Image file picker for the toolbar button (PRD F-IMG-02).</summary>
    Task<string?> PickImageAsync();

    /// <summary>Copy into the assets folder or reference the original path (PRD F-IMG-02).</summary>
    Task<ImageInsertMode> AskImageInsertModeAsync(string fileName);

    Task<DiscardChoice> ConfirmDiscardAsync(string fileName);

    Task<bool> ConfirmAsync(string title, string message, string primaryText);

    void ShowInfo(string message, bool isError = false);
}
