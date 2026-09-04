using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace MarkPad.App.Dialogs;

/// <summary>도움말 › 정보 (PRD 4.1 메뉴바의 [도움말]): version, runtime, shortcut cheat sheet, third-party notices.</summary>
public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog(string? editorVersion, string? webView2Version)
    {
        InitializeComponent();
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0";
        VersionText.Text = $"MarkPad {version}";
        RuntimeText.Text = $"편집기 번들 {editorVersion ?? "?"} · WebView2 {webView2Version ?? "미설치"} · .NET {Environment.Version} · Windows {Environment.OSVersion.Version}";
    }

    private async void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(App.LocalDataDir, "logs");
        if (Directory.Exists(dir)) await Launcher.LaunchFolderPathAsync(dir);
    }
}
