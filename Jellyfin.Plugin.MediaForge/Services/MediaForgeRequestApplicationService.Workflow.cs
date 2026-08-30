using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.MediaForge.Models;

namespace Jellyfin.Plugin.MediaForge.Services;

public sealed partial class MediaForgeRequestApplicationService
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _autosyncLock = new(1, 1);
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);

    private async Task<IReadOnlyList<MediaForgeProgressInfo>> ReadProgressBatchesAsync(long[] ids, CancellationToken token)
    {
        var result = new List<MediaForgeProgressInfo>();
        foreach (var batch in ids.Distinct().Chunk(200))
            result.AddRange(ReadProgress(await _mediaForge.GetProgressAsync(batch, token).ConfigureAwait(false), batch));
        return result;
    }

    public async Task EnsureAutosyncAsync(long id, CancellationToken token)
    {
        await _autosyncLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var item = await _store.GetAsync(id, token).ConfigureAwait(false);
            if (item is null || !item.AutosyncRequested || item.AutosyncJobId.HasValue
                || item.Status is not (RequestStatuses.Queued or RequestStatuses.Completed or RequestStatuses.Available or RequestStatuses.Shared)) return;
            var related = await _store.SnapshotAsync(token).ConfigureAwait(false);
            if (item.SharedRequestIds.Any(id => related.FirstOrDefault(r => r.Id == id)?.Status is not (RequestStatuses.Queued or RequestStatuses.Completed or RequestStatuses.Available))) return;
            try
            {
                var rule = await _store.GetRuleAsync(item.UserId, token).ConfigureAwait(false);
                if (!rule.AllowSubscriptions || !await SourceIsAllowedAsync(item.Source, "series", token).ConfigureAwait(false))
                    throw new MediaForgeApplicationException(HttpStatusCode.Forbidden, "Autosync ist durch die Benutzer- oder Quellenregeln gesperrt.");
                var result = await _mediaForge.EnsureAutosyncAsync(item, token).ConfigureAwait(false);
                if (!result.TryGetProperty("job_id", out var value) || !value.TryGetInt64(out var jobId) || jobId <= 0)
                    throw new MediaForgeApplicationException(HttpStatusCode.BadGateway, "MediaForge hat kein gültiges Autosync-Abo bestätigt.");
                var restricted = !result.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True
                    || (result.TryGetProperty("on_hold", out var hold) && hold.ValueKind == JsonValueKind.True)
                    || (result.TryGetProperty("filtered", out var filtered) && filtered.ValueKind == JsonValueKind.True);
                await _store.UpdateWorkflowAsync(id, row =>
                {
                    row.AutosyncJobId = jobId; row.AutosyncStatus = "ready"; row.AutosyncRestricted = restricted;
                    row.AutosyncError = null; row.AutosyncNextAttemptUtc = null;
                    row.History.Add(new RequestEvent("autosync-ready", DateTime.UtcNow, "MediaForge"));
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (error is MediaForgeException or MediaForgeApplicationException or HttpRequestException or OperationCanceledException)
            {
                var message = error is MediaForgeException { StatusCode: HttpStatusCode.NotFound }
                    ? "Das MediaForge-Modul benötigt ein Update für Autosync."
                    : error is MediaForgeApplicationException appError ? appError.Message : "Autosync konnte noch nicht bestätigt werden. Die Übernahme wird wiederholt.";
                await _store.UpdateWorkflowAsync(id, row =>
                {
                    row.AutosyncStatus = "retry"; row.AutosyncError = message;
                    row.AutosyncNextAttemptUtc = DateTime.UtcNow.AddMinutes(row.AutosyncAttempts++ switch { 0 => 1, 1 => 5, 2 => 15, _ => 60 });
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally { _autosyncLock.Release(); }
    }

    public async Task<MediaRequest?> ReconcileAsync(long id, string actor, bool confirmResend, CancellationToken token)
    {
        await _recoveryLock.WaitAsync(token).ConfigureAwait(false);
        try { return await ReconcileCoreAsync(id, actor, confirmResend, token).ConfigureAwait(false); }
        finally { _recoveryLock.Release(); }
    }

    public async Task<MediaRequest?> RetryMissingAsync(long id, string actor, CancellationToken token)
    {
        await _recoveryLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var item = await _store.GetAsync(id, token).ConfigureAwait(false);
            if (item?.Status is not (RequestStatuses.Failed or RequestStatuses.Partial or RequestStatuses.Cancelled)) return item;
            if (item.Status != RequestStatuses.Failed)
                await _store.UpdateWorkflowAsync(id, row => { row.Status = RequestStatuses.Failed; row.HandoffStarted = false; row.OperationId = Guid.NewGuid().ToString("N"); }, token).ConfigureAwait(false);
            return await ApproveAsync(id, actor, token).ConfigureAwait(false);
        }
        finally { _recoveryLock.Release(); }
    }

    private async Task<MediaRequest?> ReconcileCoreAsync(long id, string actor, bool confirmResend, CancellationToken token)
    {
        var item = await _store.GetAsync(id, token).ConfigureAwait(false);
        if (item is null || item.Status != RequestStatuses.Uncertain) return item;
        try
        {
            var receipt = await _mediaForge.GetOperationAsync(item.OperationId, token).ConfigureAwait(false);
            if (receipt.TryGetProperty("state", out var state) && state.GetString() == "confirmed"
                && receipt.TryGetProperty("queue_id", out var queue) && queue.TryGetInt64(out var queueId) && queueId > 0)
            {
                await _store.MarkQueuedAsync(id, queueId, actor, CancellationToken.None).ConfigureAwait(false);
                await _store.UpdateWorkflowAsync(id, row => row.MediaForgeQueueIds = [queueId], CancellationToken.None).ConfigureAwait(false);
                await EnsureAutosyncAsync(id, token).ConfigureAwait(false);
                return await _store.GetAsync(id, token).ConfigureAwait(false);
            }
        }
        catch (MediaForgeException) { /* An unavailable receipt never proves the write failed. */ }
        if (confirmResend)
        {
            await _store.UpdateWorkflowAsync(id, row =>
            {
                row.History.Add(new RequestEvent("resend-confirmed", DateTime.UtcNow, actor, "Administrator bestätigt mögliches Download-Duplikat."));
                row.Status = RequestStatuses.Failed; row.HandoffStarted = false; row.OperationId = Guid.NewGuid().ToString("N");
            }, CancellationToken.None).ConfigureAwait(false);
            return await ApproveAsync(id, actor, token).ConfigureAwait(false);
        }
        return await _store.GetAsync(id, token).ConfigureAwait(false);
    }

    public async Task SynchronizeAsync(bool checkLibrary, CancellationToken token)
    {
        if (!await _syncLock.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            await _store.MaintainAsync(token).ConfigureAwait(false);
            var items = await _store.SnapshotAsync(token).ConfigureAwait(false);
            var ids = items.Where(r => r.Status == RequestStatuses.Queued && r.MediaForgeQueueId.HasValue).Select(r => r.MediaForgeQueueId!.Value).ToArray();
            if (ids.Length > 0)
            {
                try
                {
                    var states = await ReadProgressBatchesAsync(ids, token).ConfigureAwait(false);
                    var map = states.ToDictionary(s => s.QueueId, s => s.Status);
                    foreach (var user in items.Select(r => r.UserId).Distinct())
                        await _store.SyncQueueStatesAsync(user, map, token).ConfigureAwait(false);
                    lock (_liveProgress)
                    {
                        _liveProgress.Clear();
                        foreach (var state in states) _liveProgress[state.QueueId] = state;
                    }
                }
                catch (MediaForgeException) { /* Continue independent local/library work while upstream is down. */ }
            }
            items = await _store.SnapshotAsync(token).ConfigureAwait(false);
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.Status == RequestStatuses.Uncertain)
                    await ReconcileAsync(item.Id, "recovery", false, token).ConfigureAwait(false);
                if (item.SharedRequestIds.Count > 0 && item.Status is RequestStatuses.Shared or RequestStatuses.Completed)
                {
                    var dependencies = items.Where(r => item.SharedRequestIds.Contains(r.Id)).ToArray();
                    var newStatus = dependencies.Any(r => r.Status == RequestStatuses.Rejected) ? RequestStatuses.Rejected
                        : dependencies.Length != item.SharedRequestIds.Count || dependencies.Any(r => r.Status is RequestStatuses.Failed or RequestStatuses.Withdrawn or RequestStatuses.Cancelled or RequestStatuses.Partial)
                        ? RequestStatuses.Failed
                        : dependencies.All(r => r.Status is RequestStatuses.Completed or RequestStatuses.Available) ? RequestStatuses.Completed : RequestStatuses.Shared;
                    var queues = dependencies.SelectMany(r => r.MediaForgeQueueIds.Concat(r.MediaForgeQueueId.HasValue ? [r.MediaForgeQueueId.Value] : Array.Empty<long>()))
                        .Concat(item.MediaForgeQueueIds).Distinct().ToList();
                    if (item.Status != newStatus || !item.MediaForgeQueueIds.SequenceEqual(queues))
                        await _store.UpdateWorkflowAsync(item.Id, row => { row.Status = newStatus; row.MediaForgeQueueIds = queues; row.MediaForgeQueueId ??= queues.Count > 0 ? queues[0] : null; }, token).ConfigureAwait(false);
                }
                if (item.AutosyncRequested && !item.AutosyncJobId.HasValue && (!item.AutosyncNextAttemptUtc.HasValue || item.AutosyncNextAttemptUtc <= DateTime.UtcNow))
                    await EnsureAutosyncAsync(item.Id, token).ConfigureAwait(false);
                if (checkLibrary && item.LibraryIdentity is not null && item.Status is RequestStatuses.Completed or RequestStatuses.Available)
                {
                    if (!_libraryAvailability.CanAccess(item.LibraryIdentity, item.UserId)) continue;
                    var availability = _libraryAvailability.GetUserAvailability(item.LibraryIdentity, item.UserId);
                    var available = availability.ItemExists && (item.LibraryIdentity.IsMovie || item.ExpectedEpisodes.All(availability.Episodes.Contains));
                    await _store.ObserveLibraryAsync(item.Id, availability.Episodes, available, token).ConfigureAwait(false);
                }
            }
        }
        finally { _syncLock.Release(); }
    }

    private readonly Dictionary<long, MediaForgeProgressInfo> _liveProgress = new();

    public void AddCachedProgress(IEnumerable<MediaRequest> items)
    {
        lock (_liveProgress)
        {
            foreach (var item in items)
            {
                item.AvailableActions = item.Status == RequestStatuses.Pending ? ["withdrawParticipation"]
                    : item.Status == RequestStatuses.Available ? ["openLibrary"] : [];
                var ids = item.MediaForgeQueueIds.Concat(item.MediaForgeQueueId.HasValue ? [item.MediaForgeQueueId.Value] : Array.Empty<long>()).Distinct();
                var states = ids.Where(_liveProgress.ContainsKey).Select(id => _liveProgress[id]).ToArray();
                if (states.Length == 0) continue;
                item.Progress = (int)Math.Round(states.Average(s => s.Percent));
                item.QueueRunning = states.Any(s => s.Status == "running");
            }
        }
    }

    public static void AddAdminActions(IEnumerable<MediaRequest> items)
    {
        foreach (var item in items)
        {
            item.AvailableActions = item.Status switch
            {
                RequestStatuses.Pending or RequestStatuses.Failed => ["approve", "reject"],
                RequestStatuses.Uncertain => ["reconcile", "confirmResend"],
                RequestStatuses.Partial or RequestStatuses.Cancelled => ["retryMissing"],
                _ => [],
            };
            if (item.AutosyncRequested && !item.AutosyncJobId.HasValue && item.Status is RequestStatuses.Queued or RequestStatuses.Shared or RequestStatuses.Completed or RequestStatuses.Available)
                item.AvailableActions.Add("retryAutosync");
        }
    }
}
