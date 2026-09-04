using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkPad.App.Services;

namespace MarkPad.App.ViewModels;

/// <summary>One folder or file in the sidebar tree; children are loaded on first expand (PRD F-FILE-09).</summary>
public sealed partial class FileEntry : ObservableObject
{
    public FileEntry(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
        Name = System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path;
        if (isDirectory) Children.Add(Placeholder);
    }

    public static FileEntry Placeholder { get; } = new("…", isDirectory: false) { IsPlaceholder = true };

    public string Path { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; init; }
    public string Glyph => IsDirectory ? "" : "";
    public ObservableCollection<FileEntry> Children { get; } = [];
    private bool _loaded;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureLoaded();
    }

    public void EnsureLoaded()
    {
        if (_loaded || !IsDirectory) return;
        _loaded = true;
        Children.Clear();
        foreach (var child in FileSidebarViewModel.Enumerate(Path)) Children.Add(child);
    }
}

/// <summary>Folder tree of the active document (md/markdown/txt only), toggled with Ctrl+Shift+B (T-53).</summary>
public sealed partial class FileSidebarViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string? RootPath { get; set; }

    public ObservableCollection<FileEntry> Roots { get; } = [];

    public event EventHandler<string>? OpenRequested;

    public void SetRoot(string? directory)
    {
        if (string.Equals(directory, RootPath, StringComparison.OrdinalIgnoreCase)) return;
        RootPath = directory;
        Roots.Clear();
        if (directory is null || !Directory.Exists(directory)) return;
        var root = new FileEntry(directory, isDirectory: true) { IsExpanded = true };
        root.EnsureLoaded();
        Roots.Add(root);
    }

    public void Refresh()
    {
        var r = RootPath;
        RootPath = null;
        SetRoot(r);
    }

    public void RequestOpen(FileEntry entry)
    {
        if (!entry.IsDirectory && !entry.IsPlaceholder) OpenRequested?.Invoke(this, entry.Path);
    }

    internal static IEnumerable<FileEntry> Enumerate(string directory)
    {
        IEnumerable<string> dirs;
        IEnumerable<string> files;
        try
        {
            dirs = Directory.EnumerateDirectories(directory).Where(d => !IsHidden(d)).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(directory).Where(f => ActivationService.IsOpenable(f) && !IsHidden(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var d in dirs) yield return new FileEntry(d, isDirectory: true);
        foreach (var f in files) yield return new FileEntry(f, isDirectory: false);
    }

    private static bool IsHidden(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.')) return true;
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
