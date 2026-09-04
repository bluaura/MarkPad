using Microsoft.Windows.ApplicationModel.Resources;

namespace MarkPad.App.Services;

/// <summary>
/// Code-side access to Strings/{lang}/Resources.resw (PRD 7.5, T-60). XAML uses x:Uid directly.
/// Falls back to the key when the PRI cannot be resolved (e.g. odd unpackaged layouts) so the UI never shows blanks.
/// </summary>
public static class Loc
{
    private static readonly Lazy<ResourceLoader?> s_loader = new(() =>
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or FileNotFoundException)
        {
            return null;
        }
    });

    private static readonly Dictionary<string, string> s_fallback = new(StringComparer.Ordinal)
    {
        ["Dialog_UnsavedTitle"] = "저장되지 않은 변경 사항",
        ["Dialog_UnsavedBody"] = "'{0}'의 변경 사항을 저장할까요?",
        ["Dialog_Save"] = "저장",
        ["Dialog_DontSave"] = "저장 안 함",
        ["Dialog_Cancel"] = "취소",
        ["Status_Saved"] = "저장됨 ✓",
        ["Status_Modified"] = "● 수정됨",
        ["Status_ReadOnly"] = "읽기 전용",
        ["Title_Untitled"] = "Untitled",
    };

    public static string Get(string key)
    {
        try
        {
            var v = s_loader.Value?.GetString(key);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
        }
        return s_fallback.TryGetValue(key, out var f) ? f : key;
    }

    public static string Format(string key, params object[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
