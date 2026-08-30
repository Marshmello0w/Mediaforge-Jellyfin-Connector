using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaForge.Services;

public sealed class RequestSynchronizationService(
    MediaForgeRequestApplicationService application,
    ILibraryManager library,
    ILogger<RequestSynchronizationService> logger) : BackgroundService
{
    private int _libraryChanged = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        library.ItemAdded += OnLibraryChanged;
        library.ItemUpdated += OnLibraryChanged;
        var nextLibraryCheck = DateTime.MinValue;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                var check = Interlocked.Exchange(ref _libraryChanged, 0) != 0 || DateTime.UtcNow >= nextLibraryCheck;
                try
                {
                    await application.SynchronizeAsync(check, stoppingToken).ConfigureAwait(false);
                    if (check) nextLibraryCheck = DateTime.UtcNow.AddMinutes(5);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    // Do not log exception messages: upstream errors may
                    // contain credentials, paths, or response bodies.
                    logger.LogWarning("MediaForge background synchronization failed ({ErrorType})", error.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        finally
        {
            library.ItemAdded -= OnLibraryChanged;
            library.ItemUpdated -= OnLibraryChanged;
        }
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs args) => Interlocked.Exchange(ref _libraryChanged, 1);
}
