using MarkPad.Core.Mru;
using Microsoft.Extensions.Logging;
using Windows.UI.StartScreen;

namespace MarkPad.App.Services;

/// <summary>Taskbar jump list "Recent" (PRD F-FILE-04). Only packaged apps support it; unpackaged runs are a no-op.</summary>
public sealed class JumpListService
{
    private readonly ILogger<JumpListService> _log;
    private bool _unsupportedLogged;

    public JumpListService(ILogger<JumpListService> log)
    {
        _log = log;
    }

    public async Task UpdateAsync(IReadOnlyList<RecentFile> items)
    {
        bool supported;
        try
        {
            supported = JumpList.IsSupported();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            supported = false;
        }
        if (!supported)
        {
            if (!_unsupportedLogged)
            {
                _unsupportedLogged = true;
                _log.LogDebug("jump list not supported (unpackaged)");
            }
            return;
        }

        try
        {
            var list = await JumpList.LoadCurrentAsync();
            list.SystemGroupKind = JumpListSystemGroupKind.None;
            list.Items.Clear();
            foreach (var f in items.Take(10))
            {
                var item = JumpListItem.CreateWithArguments($"\"{f.Path}\"", f.Name);
                item.Description = f.Directory;
                item.GroupName = "최근 파일";
                list.Items.Add(item);
            }
            await list.SaveAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "jump list update failed");
        }
    }
}
