using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.MediaForge.Models;
using Jellyfin.Plugin.MediaForge.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MediaForge.Api;

[ApiController]
[Route("MediaForgeRequests")]
[Authorize]
[Produces("application/json")]
[RequestSizeLimit(65536)]
public sealed class WorkflowController(RequestStore store, MediaForgeRequestApplicationService application,
    MediaForgeClient client, IUserManager users, JellyfinLibraryAvailabilityService library, UserRateLimiter limiter, MediaAccessGrantStore grants) : ControllerBase
{
    private string UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("Jellyfin-UserId") ?? User.FindFirstValue("UserId"), out var id)
        ? id.ToString("N") : throw new UnauthorizedAccessException("Authenticated Jellyfin user required.");
    private string Actor => User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "admin";

    [HttpGet("Notifications")]
    public async Task<IActionResult> Notifications(CancellationToken token)
    {
        var result = await store.NotificationsAsync(UserId, token).ConfigureAwait(false);
        return Ok(new { items = result.Items, unread = result.Unread, preferences = result.Preferences });
    }

    [HttpPost("Notifications/Read")]
    public async Task<IActionResult> Read([FromBody] ReadNotification body, CancellationToken token)
    {
        await store.UpdateNotificationsAsync(UserId, body.Id, null, token).ConfigureAwait(false);
        return Ok(new { ok = true });
    }

    [HttpPut("Notifications/Preferences")]
    public async Task<IActionResult> Preferences([FromBody] NotificationPreferences body, CancellationToken token)
    {
        if (body.NewEpisodes is not ("daily" or "immediate" or "off")) return BadRequest(new { error = "Ungültiger Benachrichtigungsmodus." });
        await store.UpdateNotificationsAsync(UserId, null, body, token).ConfigureAwait(false);
        return Ok(body);
    }

    [HttpGet("Requests/{id:long}/Library")]
    public async Task<IActionResult> Library(long id, CancellationToken token)
    {
        var item = await store.GetAsync(id, token).ConfigureAwait(false);
        if (item is null || item.UserId != UserId) return NotFound();
        var user = users.GetUserById(Guid.Parse(UserId));
        var itemId = user is null || item.LibraryIdentity is null ? null : library.GetAccessibleItemId(item.LibraryIdentity, user);
        return Ok(new { itemId });
    }

    [HttpPost("Requests/Participation")]
    public async Task<IActionResult> Participate([FromBody] AutomaticMediaRequest request, CancellationToken token)
    {
        // The client sends its granted selection, not another user's ID.
        // Shared matching is atomic and never discloses a foreign request.
        try
        {
            var result = await application.SubmitAutomaticAsync(UserId, Actor, request, true, token).ConfigureAwait(false);
            return result.Disposition switch
            {
                SubmitDisposition.Stored or SubmitDisposition.Queued => Ok(result.Request),
                SubmitDisposition.Duplicate => Ok(result.Duplicate),
                SubmitDisposition.LimitReached or SubmitDisposition.StoreCapacityReached => StatusCode(429, new { error = "Anfragelimit erreicht." }),
                SubmitDisposition.AlreadyAvailable => Ok(new { status = "available" }),
                _ => StatusCode(502, new { error = "Die Übergabe konnte nicht bestätigt werden." }),
            };
        }
        catch (MediaForgeApplicationException error) { return StatusCode((int)error.StatusCode, new { error = error.Message }); }
        catch (MediaForgeException error) { return StatusCode((int)error.StatusCode, new { error = error.Message }); }
    }

    [HttpPost("Requests/Matching")]
    public async Task<IActionResult> Matching([FromBody] AutomaticMediaRequest request, CancellationToken token)
    {
        if (!grants.IsGranted(UserId, request.SeriesUrl, out var source) || !string.Equals(source, request.Source, StringComparison.OrdinalIgnoreCase)) return BadRequest();
        if (!limiter.TryConsume(UserId, "matching", 30, TimeSpan.FromMinutes(1))) return StatusCode(429);
        var all = await store.SnapshotAsync(token).ConfigureAwait(false);
        return Ok(new { exists = all.Any(r => r.SeriesUrl.TrimEnd('/') == request.SeriesUrl.TrimEnd('/') && r.Source == source
            && r.Language == request.Language && r.Provider == request.Provider && r.Upscale == request.Upscale
            && r.Status is RequestStatuses.Pending or RequestStatuses.Processing or RequestStatuses.Queued or RequestStatuses.Shared) });
    }

    [HttpGet("Admin/Overview")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Overview([MaxLength(300)] string? query, string? userId, string? status, string? source,
        DateTime? since, int page = 1, int pageSize = 30, CancellationToken token = default)
    {
        var result = await store.AdminPageAsync(query, userId, status, source, since, page, pageSize, token).ConfigureAwait(false);
        application.AddCachedProgress(result.Items);
        MediaForgeRequestApplicationService.AddAdminActions(result.Items);
        var all = await store.SnapshotAsync(token).ConfigureAwait(false);
        var participants = result.Items.ToDictionary(r => r.Id, r => all.Where(other => other.Id == r.Id || other.SharedRequestIds.Contains(r.Id))
            .Select(other => new { userId = other.UserId, username = other.Username, requestId = other.Id }).ToArray());
        return Ok(new { items = result.Items, total = result.Total, page = result.Page, pageSize = result.PageSize,
            pending = result.Pending, downloading = result.Downloading, errors = result.Errors, autosyncPending = result.AutosyncPending, participants });
    }

    [HttpPost("Admin/Batch")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Batch([FromBody] BatchDecision body, CancellationToken token)
    {
        body.Reason ??= "";
        if (body.Ids is null || body.Ids.Count is < 1 or > 50 || body.Ids.Distinct().Count() != body.Ids.Count || body.Ids.Any(id => id <= 0)
            || body.Action is not ("approve" or "reject") || body.Reason.Any(char.IsControl)) return BadRequest();
        if (!limiter.TryConsume(UserId, "batch", 5, TimeSpan.FromMinutes(1))) return StatusCode(429);
        var results = new List<object>();
        foreach (var id in body.Ids)
        {
            try
            {
                var existing = await store.GetAsync(id, token).ConfigureAwait(false);
                if (existing is null) { results.Add(new { id, ok = false, error = "Anfrage nicht gefunden." }); continue; }
                if (body.Action == "reject")
                {
                    var rejected = await application.RejectAsync(id, body.Reason, Actor, token).ConfigureAwait(false);
                    results.Add(new { id, ok = rejected, error = rejected ? null : "Status erlaubt keine Ablehnung." });
                }
                else
                {
                    var result = await application.ApproveAsync(id, Actor, token).ConfigureAwait(false);
                    results.Add(new { id, ok = result.Status is RequestStatuses.Queued or RequestStatuses.Available or RequestStatuses.Shared, status = result.Status, error = result.Error });
                }
            }
            catch (Exception error) when (error is MediaForgeApplicationException or MediaForgeException or IOException)
            { results.Add(new { id, ok = false, error = "Vorgang fehlgeschlagen; gespeicherten Status prüfen." }); }
        }
        return Ok(new { results });
    }

    [HttpPost("Admin/Requests/{id:long}/Recovery")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Recovery(long id, [FromBody] RecoveryAction body, CancellationToken token)
    {
        if (!limiter.TryConsume(UserId, "recovery", 10, TimeSpan.FromMinutes(1))) return StatusCode(429);
        var item = await store.GetAsync(id, token).ConfigureAwait(false);
        if (item is null) return NotFound();
        if (body.Action == "autosync") await application.EnsureAutosyncAsync(id, token).ConfigureAwait(false);
        else if (body.Action == "reconcile") await application.ReconcileAsync(id, Actor, body.ConfirmPossibleDuplicate, token).ConfigureAwait(false);
        else if (body.Action == "missing")
        {
            if (item.Status is not (RequestStatuses.Failed or RequestStatuses.Partial or RequestStatuses.Cancelled)) return Conflict();
            await application.RetryMissingAsync(id, Actor, token).ConfigureAwait(false);
        }
        else return BadRequest();
        await store.UpdateWorkflowAsync(id, r => r.History.Add(new RequestEvent("recovery-" + body.Action, DateTime.UtcNow, Actor)), token).ConfigureAwait(false);
        return Ok(await store.GetAsync(id, token).ConfigureAwait(false));
    }

    [HttpGet("Admin/Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Users(CancellationToken token)
    {
        var result = new List<object>();
        foreach (var user in users.GetUsers())
        {
            var id = user.Id.ToString("N");
            result.Add(new { id, username = user.Username, rule = await store.GetRuleAsync(id, token).ConfigureAwait(false) });
        }
        return Ok(result);
    }

    [HttpPut("Admin/Users/{userId}/Rule")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Rule(string userId, [FromBody] UserRequestRule body, CancellationToken token)
    {
        if (!Guid.TryParse(userId, out var id) || users.GetUserById(id) is null) return NotFound();
        if (body.ApprovalMode is not ("inherit" or "manual" or "automatic") || body.MaxOpenRequests is < 1 or > 100) return BadRequest();
        await store.SetRuleAsync(id.ToString("N"), body, Actor, token).ConfigureAwait(false);
        return Ok(body);
    }

    [HttpGet("Admin/Diagnostics")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> Diagnostics(CancellationToken token)
    {
        var health = await client.CheckHealthAsync(token).ConfigureAwait(false);
        object? module = null;
        string? error = null;
        if (health.Healthy)
        {
            try
            {
                var response = await client.GetHealthAsync(token).ConfigureAwait(false);
                var version = response.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: <= 40 } text && !text.Any(char.IsControl) ? text : "unknown";
                var capabilities = response.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                    ? caps.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String && c.GetString() is "autosync" or "download-receipts").Select(c => c.GetString()).Distinct().ToArray() : [];
                var permissions = new Dictionary<string, bool?>();
                foreach (var scope in new[] { "status:read", "library:read", "queue:read", "queue:write" })
                    permissions[scope] = response.TryGetProperty("permissions", out var scopes) && scopes.ValueKind == JsonValueKind.Object
                        && scopes.TryGetProperty(scope, out var allowed) && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False ? allowed.GetBoolean() : null;
                module = new { version, capabilities, permissions };
            }
            catch (MediaForgeException) { error = "Modulstatus konnte nicht gelesen werden."; }
        }
        return Ok(new { connection = new { healthy = health.Healthy, configured = health.Configured, apiKeyValid = health.ApiKeyValid },
            pluginVersion = typeof(WorkflowController).Assembly.GetName().Version?.ToString(), module, error });
    }
}

public sealed class ReadNotification { [Required, MaxLength(32)] public string Id { get; set; } = "all"; }
public sealed class BatchDecision
{
    public List<long> Ids { get; set; } = [];
    [Required] public string Action { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
}
public sealed class RecoveryAction
{
    [Required] public string Action { get; set; } = "";
    public bool ConfirmPossibleDuplicate { get; set; }
}
