using MarkPad.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MarkPad.App;

/// <summary>Custom entry point (DISABLE_XAML_GENERATED_MAIN) so single-instance redirection runs before XAML starts.</summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (ActivationService.TryRedirectToMainInstance())
        {
            return 0;
        }

        Application.Start(static p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
