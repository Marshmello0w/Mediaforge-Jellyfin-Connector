using System.Text.Json;
using Jellyfin.Plugin.MediaForge.Models;

namespace Jellyfin.Plugin.MediaForge.Services;

public sealed partial class RequestStore
{
    private static bool SameOptions(MediaRequest item, CreateMediaRequest request)
        => item.SeriesUrl.TrimEnd('/') == request.SeriesUrl.TrimEnd('/')
        && item.Source == request.Source && item.Language == request.Language
        && item.Provider == request.Provider && item.Upscale == request.Upscale;

    private static void PrepareSharing(StoreDocument document, MediaRequest item)
    {
        var remaining = item.Episodes.ToHashSet(StringComparer.Ordinal);
        item.SharedRequestIds.Clear();
        foreach (var candidate in document.Requests.Where(other => other.Id < item.Id && (IsOpen(other) || other.Status == RequestStatuses.Completed)
            && other.SeriesUrl.TrimEnd('/') == item.SeriesUrl.TrimEnd('/') && other.Source == item.Source
            && other.Language == item.Language && other.Provider == item.Provider && other.Upscale == item.Upscale))
        {
            if (!remaining.Overlaps(candidate.Episodes)) continue;
            remaining.ExceptWith(candidate.Episodes);
            item.SharedRequestIds.Add(candidate.Id);
        }
        item.EpisodesJson = JsonSerializer.Serialize(remaining.Order(StringComparer.Ordinal));
    }

    private static void RecordEvent(StoreDocument document, MediaRequest item, string kind, string actor, string? detail = null)
    {
        if (item.History.LastOrDefault()?.Kind == kind && detail is null) return;
        item.History.Add(new RequestEvent(kind, DateTime.UtcNow, actor, detail));
        if (item.History.Count > 200) item.History.RemoveAt(0);
        if (!item.ModernWorkflow || item.WithdrawnByOwner) return;
        var preferences = document.NotificationPreferences.GetValueOrDefault(item.UserId) ?? new();
        var message = kind switch
        {
            "approved" or RequestStatuses.Shared when preferences.Decisions => "Deine Anfrage wurde freigegeben.",
            RequestStatuses.Rejected when preferences.Decisions => "Deine Anfrage wurde abgelehnt." + (string.IsNullOrEmpty(item.Error) ? "" : " " + item.Error),
            _ => null,
        };
        if (message is not null) Notify(document, item, kind, message);
    }

    private static void Notify(StoreDocument document, MediaRequest item, string key, string message)
    {
        if (item.WithdrawnByOwner) return;
        var unique = item.Id + ":" + key;
        if (document.Notifications.Any(n => n.UserId == item.UserId && n.Key == unique)) return;
        document.Notifications.Add(new UserNotification { UserId = item.UserId, RequestId = item.Id, Key = unique, Message = item.Title + ": " + message });
    }

    public async Task UpdateWorkflowAsync(long id, Action<MediaRequest> update, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var document = CloneDocument();
            var item = document.Requests.FirstOrDefault(r => r.Id == id);
            if (item is null) return;
            var previous = item.Status;
            update(item);
            if (item.Status != previous) RecordEvent(document, item, item.Status, "system");
            await SaveLockedAsync(document, token).ConfigureAwait(false);
            _document = document;
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<MediaRequest>> SnapshotAsync(CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try { return CloneDocument().Requests; }
        finally { _lock.Release(); }
    }

    public async Task<UserRequestRule> GetRuleAsync(string userId, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try { return CloneDocument().UserRules.GetValueOrDefault(userId) ?? new(); }
        finally { _lock.Release(); }
    }

    public async Task SetRuleAsync(string userId, UserRequestRule rule, string actor, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var document = CloneDocument();
            document.UserRules[userId] = rule;
            // Rules are administrative configuration; retain a bounded audit
            // trail without exposing this information in personal requests.
            document.Audit.Add(new RequestEvent("user-rule", DateTime.UtcNow, actor, userId));
            if (document.Audit.Count > 2000) document.Audit.RemoveAt(0);
            await SaveLockedAsync(document, token).ConfigureAwait(false);
            _document = document;
        }
        finally { _lock.Release(); }
    }

    public async Task<(IReadOnlyList<UserNotification> Items, NotificationPreferences Preferences, int Unread)> NotificationsAsync(string userId, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var document = CloneDocument();
            return (document.Notifications.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedUtc).Take(200).ToArray(),
                document.NotificationPreferences.GetValueOrDefault(userId) ?? new(), document.Notifications.Count(n => n.UserId == userId && !n.ReadUtc.HasValue));
        }
        finally { _lock.Release(); }
    }

    public async Task UpdateNotificationsAsync(string userId, string? readId, NotificationPreferences? preferences, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var document = CloneDocument();
            if (preferences is not null) document.NotificationPreferences[userId] = preferences;
            if (readId is not null)
                foreach (var item in document.Notifications.Where(n => n.UserId == userId && (readId == "all" || n.Id == readId)))
                    item.ReadUtc ??= DateTime.UtcNow;
            await SaveLockedAsync(document, token).ConfigureAwait(false);
            _document = document;
        }
        finally { _lock.Release(); }
    }

    public async Task ObserveLibraryAsync(long id, IReadOnlySet<LibraryEpisodeKey> episodes, bool available, CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = _document.Requests.FirstOrDefault(r => r.Id == id);
            if (current is null || (current.SeenEpisodes is not null && episodes.SetEquals(current.SeenEpisodes)
                && current.DigestEpisodes.Count == 0 && (current.Status != RequestStatuses.Completed || !available))) return;
            var document = CloneDocument();
            var item = document.Requests.FirstOrDefault(r => r.Id == id);
            if (item is null) return;
            if (available && item.Status == RequestStatuses.Completed)
            {
                item.Status = RequestStatuses.Available;
                RecordEvent(document, item, item.Status, "Jellyfin");
            }
            var preferences = document.NotificationPreferences.GetValueOrDefault(item.UserId) ?? new();
            if (available && item.ModernWorkflow && !item.SubscribeOnly && preferences.Availability)
                Notify(document, item, "available", "Der angefragte Inhalt ist jetzt in Jellyfin verfügbar.");
            if (item.AutosyncJobId.HasValue && item.SeenEpisodes is not null)
                item.DigestEpisodes = item.DigestEpisodes.Concat(episodes.Except(item.SeenEpisodes).Except(item.ExpectedEpisodes)).Distinct().ToList();
            item.SeenEpisodes = episodes.ToList();
            if (preferences.NewEpisodes == "off") item.DigestEpisodes.Clear();
            if (item.DigestEpisodes.Count > 0 && (preferences.NewEpisodes == "immediate" || item.LastDigestUtc.GetValueOrDefault(item.CreatedUtc).Date < DateTime.UtcNow.Date))
            {
                var key = "episodes:" + item.SeriesUrl.TrimEnd('/') + ":" + (preferences.NewEpisodes == "daily" ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                    : string.Join(",", item.DigestEpisodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)));
                if (!item.WithdrawnByOwner)
                {
                    var notification = document.Notifications.FirstOrDefault(n => n.UserId == item.UserId && n.Key == key);
                    if (notification is null)
                    {
                        notification = new UserNotification { UserId = item.UserId, RequestId = item.Id, Key = key };
                        document.Notifications.Add(notification);
                    }
                    notification.Episodes = notification.Episodes.Concat(item.DigestEpisodes).Distinct().ToList();
                    notification.Message = item.Title + $": {notification.Episodes.Count} neue Folgen sind in Jellyfin verfügbar.";
                }
                item.DigestEpisodes.Clear();
                item.LastDigestUtc = DateTime.UtcNow;
            }
            document.Notifications.RemoveAll(n => n.CreatedUtc < DateTime.UtcNow.AddDays(n.ReadUtc.HasValue ? -90 : -180));
            await SaveLockedAsync(document, token).ConfigureAwait(false);
            _document = document;
        }
        finally { _lock.Release(); }
    }

    public async Task MaintainAsync(CancellationToken token)
    {
        await _lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_document.Notifications.Any(n => n.CreatedUtc < DateTime.UtcNow.AddDays(n.ReadUtc.HasValue ? -90 : -180))) return;
            var document = CloneDocument();
            document.Notifications.RemoveAll(n => n.CreatedUtc < DateTime.UtcNow.AddDays(n.ReadUtc.HasValue ? -90 : -180));
            await SaveLockedAsync(document, token).ConfigureAwait(false);
            _document = document;
        }
        finally { _lock.Release(); }
    }

    public async Task<AdminRequestPage> AdminPageAsync(string? query, string? userId, string? status, string? source, DateTime? since, int page, int pageSize, CancellationToken token)
    {
        var all = await SnapshotAsync(token).ConfigureAwait(false);
        var filtered = all.Where(r => (string.IsNullOrWhiteSpace(query) || r.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(userId) || r.UserId == userId) && (string.IsNullOrEmpty(status) || r.Status == status)
            && (string.IsNullOrEmpty(source) || r.Source == source) && (!since.HasValue || r.CreatedUtc >= since.Value));
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Clamp(page, 1, 1_000_000);
        var items = filtered.Select(r =>
            {
                var primary = r.SharedRequestIds.Count == 1 && r.Episodes.Count == 0 ? all.FirstOrDefault(p => p.Id == r.SharedRequestIds[0]) : null;
                return primary is not null && ((r.Status == RequestStatuses.Pending && primary.Status == RequestStatuses.Pending)
                    || r.Status == RequestStatuses.Shared || (r.Status == primary.Status && r.Status is RequestStatuses.Completed or RequestStatuses.Available))
                    ? primary : r;
            })
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Status is RequestStatuses.Failed or RequestStatuses.Uncertain ? 0 : r.Status == RequestStatuses.Pending ? 1 : 2)
            .ThenByDescending(r => r.Id).ToArray();
        return new(items.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), items.Length, page, pageSize,
            all.Count(r => r.Status == RequestStatuses.Pending), all.Count(r => r.Status == RequestStatuses.Queued),
            all.Count(r => r.Status is RequestStatuses.Failed or RequestStatuses.Uncertain), all.Count(r => r.AutosyncStatus is "pending" or "retry"));
    }
}
