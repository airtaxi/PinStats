using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace PinStats;

public static class Program
{
    private const string SingleInstanceKey = "PinStats-SingleInstance";

    [STAThread]
    private static async Task Main(string[] args)
    {
        // Required before calling any other Windows App SDK or WinRT API from a custom entry point.
        ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);

        // When another instance is already running, redirect the activation to it and exit this process.
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            return;
        }

        Application.Start(p =>
        {
            var dispatcherQueueSynchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(dispatcherQueueSynchronizationContext);
            _ = new App();
        });
    }
}
