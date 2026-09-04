using MarkPad.App.Editing;
using MarkPad.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkPad.App.Views;

/// <summary>Per-tab content: encoding InfoBar + the editor surface (WebView2 for markdown, TextBox for .txt).</summary>
public sealed partial class DocumentTab : UserControl
{
    private readonly ILogger<DocumentTab> _log = App.Current.Services.GetRequiredService<ILogger<DocumentTab>>();
    private bool _attached;

    public DocumentTab(DocumentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DocumentViewModel.IsReadOnly) or nameof(DocumentViewModel.EncodingDescription))
            {
                Bindings.Update();
            }
        };
        Loaded += OnLoaded;
    }

    public DocumentViewModel ViewModel { get; }

    private async Task<WebEditorSurface> CreateWebSurfaceAsync()
    {
        var host = new EditorHost();
        SurfaceHost.Child = host;
        var web = new WebEditorSurface(host);
        host.RendererCrashed += OnRendererCrashed;
        await web.InitializeAsync();
        return web;
    }

    /// <summary>ARCHITECTURE §7.1: WebView2 ProcessFailed → new surface + last snapshot (T-42).</summary>
    private async void OnRendererCrashed(object? sender, EventArgs e)
    {
        if (sender is EditorHost crashed) crashed.RendererCrashed -= OnRendererCrashed;
        try
        {
            var replacement = await CreateWebSurfaceAsync();
            await ViewModel.RecoverSurfaceAsync(replacement);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "renderer recovery failed for {Title}", ViewModel.Title);
        }
    }

    public string EncodingMessage => $"이 파일은 {ViewModel.EncodingDescription} 인코딩입니다. 편집하려면 UTF-8로 변환합니다 (저장 시 UTF-8로 기록).";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached) return;
        _attached = true;
        try
        {
            IEditorSurface surface;
            if (ViewModel.IsPlainText)
            {
                var host = new PlainTextHost();
                SurfaceHost.Child = host;
                surface = new PlainTextSurface(host);
            }
            else
            {
                surface = await CreateWebSurfaceAsync();
            }
            await ViewModel.AttachSurfaceAsync(surface);
            await ViewModel.FocusAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "surface initialization failed for {Title}", ViewModel.Title);
            SurfaceHost.Child = new TextBlock
            {
                Text = $"편집기 초기화 실패: {ex.Message}",
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
            };
        }
    }
}
