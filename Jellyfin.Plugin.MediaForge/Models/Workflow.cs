using System.Text.Json.Serialization;
using Jellyfin.Plugin.MediaForge.Services;

namespace Jellyfin.Plugin.MediaForge.Models;

public sealed partial class MediaRequest
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("handoffStarted")]
    public bool HandoffStarted { get; set; }
    [JsonPropertyName("modernWorkflow")]
    public bool ModernWorkflow { get; set; }
    [JsonPropertyName("autosyncRequested")]
    public bool AutosyncRequested { get; set; }
    [JsonPropertyName("subscribeOnly")]
    public bool SubscribeOnly { get; set; }
    [JsonPropertyName("autosyncStatus")]
    public string AutosyncStatus { get; set; } = "none";
    [JsonPropertyName("autosyncJobId")]
    public long? AutosyncJobId { get; set; }
    [JsonPropertyName("autosyncError")]
    public string? AutosyncError { get; set; }
    [JsonPropertyName("autosyncRestricted")]
    public bool AutosyncRestricted { get; set; }
    [JsonPropertyName("autosyncAttempts")]
    public int AutosyncAttempts { get; set; }
    [JsonPropertyName("autosyncNextAttemptUtc")]
    public DateTime? AutosyncNextAttemptUtc { get; set; }
    [JsonPropertyName("sharedRequestIds")]
    public List<long> SharedRequestIds { get; set; } = [];
    [JsonPropertyName("selectionEpisodes")]
    public List<string> SelectionEpisodes { get; set; } = [];
    [JsonPropertyName("withdrawnByOwner")]
    public bool WithdrawnByOwner { get; set; }
    [JsonPropertyName("mediaForgeQueueIds")]
    public List<long> MediaForgeQueueIds { get; set; } = [];
    [JsonPropertyName("history")]
    public List<RequestEvent> History { get; set; } = [];
    [JsonPropertyName("libraryIdentity")]
    public LibraryMediaIdentity? LibraryIdentity { get; set; }
    [JsonPropertyName("expectedEpisodes")]
    public List<LibraryEpisodeKey> ExpectedEpisodes { get; set; } = [];
    [JsonPropertyName("seenEpisodes")]
    public List<LibraryEpisodeKey>? SeenEpisodes { get; set; }
    [JsonPropertyName("digestEpisodes")]
    public List<LibraryEpisodeKey> DigestEpisodes { get; set; } = [];
    [JsonPropertyName("lastDigestUtc")]
    public DateTime? LastDigestUtc { get; set; }
    [JsonPropertyName("jellyfinItemId")]
    public Guid? JellyfinItemId { get; set; }
    [JsonPropertyName("availableActions")]
    public List<string> AvailableActions { get; set; } = [];
}

public sealed record RequestEvent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("utc")] DateTime Utc,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("detail")] string? Detail = null);

public sealed class UserRequestRule
{
    [JsonPropertyName("approvalMode")]
    public string ApprovalMode { get; set; } = "inherit";
    [JsonPropertyName("maxOpenRequests")]
    public int? MaxOpenRequests { get; set; }
    [JsonPropertyName("allowSubscriptions")]
    public bool AllowSubscriptions { get; set; } = true;
}

public sealed class NotificationPreferences
{
    [JsonPropertyName("decisions")]
    public bool Decisions { get; set; } = true;
    [JsonPropertyName("availability")]
    public bool Availability { get; set; } = true;
    [JsonPropertyName("newEpisodes")]
    public string NewEpisodes { get; set; } = "daily";
}

public sealed class UserNotification
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";
    [JsonPropertyName("requestId")]
    public long RequestId { get; set; }
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("readUtc")]
    public DateTime? ReadUtc { get; set; }
    [JsonPropertyName("episodes")]
    public List<LibraryEpisodeKey> Episodes { get; set; } = [];
}

public sealed record AdminRequestPage(IReadOnlyList<MediaRequest> Items, int Total, int Page, int PageSize, int Pending, int Downloading, int Errors, int AutosyncPending);
