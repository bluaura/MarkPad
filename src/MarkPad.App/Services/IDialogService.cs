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

    /// <summary>Export options dialog (F-EXP-01/02); null when cancelled.</summary>
    Task<ExportOptions?> ShowExportOptionsAsync(ExportFormat initialFormat);

    /// <summary>Save picker for an export target (.html / .pdf).</summary>
    Task<string?> PickExportPathAsync(string suggestedName, string extension, string? initialDirectory);

    Task<bool> ConfirmAsync(string title, string message, string primaryText);

    /// <summary>Settings page (F-SET-01~05); returns true when the user saved.</summary>
    Task<bool> ShowSettingsAsync();

    /// <summary>PRD F-EXP-04: put HTML + plain text on the clipboard.</summary>
    void SetClipboardRich(string html, string text);

    void ShowInfo(string message, bool isError = false);
}
