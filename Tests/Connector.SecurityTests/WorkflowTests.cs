using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.MediaForge.Api;
using Jellyfin.Plugin.MediaForge.Configuration;
using Jellyfin.Plugin.MediaForge.Models;
using Jellyfin.Plugin.MediaForge.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.AspNetCore.Authorization;

internal static class WorkflowTests
{
    private static readonly CancellationToken Token = CancellationToken.None;

    public static async Task RunAsync(string root)
    {
        await AutosyncAndRules(root);
        await CrashAndReconcile(root);
        await SharedSelections(root);
        await CompleteSubscription(root);
        await LanguageAvailability(root);
        await AutosyncDiagnostics(root);
        await PendingBadgeCount(root);
        await MigrationAndNotifications(root);
        await BackgroundAndPagination(root);
        await NewestRequestsFirst(root);
        await ResetAutomaticRules(root);
        await BatchingAndDigests(root);
        Authorization();
        var contract = JsonSerializer.Serialize(new MediaRequest { AutosyncRequested = true, History = [new("approved", DateTime.UtcNow, "Admin")] });
        Check(contract.Contains("\"autosyncRequested\":true", StringComparison.Ordinal) && contract.Contains("\"kind\":\"approved\"", StringComparison.Ordinal), "Workflow JSON depends on host naming policy.");
        Check(JsonSerializer.Serialize(new NotificationPreferences()).Contains("\"newEpisodes\"", StringComparison.Ordinal), "Notification JSON depends on host naming policy.");
        Console.WriteLine("Workflow regressions passed: autosync, rules, sharing, crash recovery, migration, notifications, background synchronization and pagination.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static (MediaForgeRequestApplicationService App, RequestStore Store, FakeMediaForgeHandler Handler) Create(string root, string name, bool automatic = false, bool complete = false)
    {
        var path = Path.Combine(root, name);
        var config = new PluginConfiguration { MediaForgeUrl = "http://localhost:8080", AutoApproveRequests = automatic };
        var secrets = new SecretStore(Path.Combine(path, "secrets")); secrets.SetApiKey("test-secret-key");
        var handler = new FakeMediaForgeHandler { SupportsReceipts = true };
        var client = new MediaForgeClient(new HttpClient(handler), secrets, () => config);
        var store = new RequestStore(path);
        var library = LibraryManagerProxy.Create(query => !complete ? [] : query.IncludeItemTypes.Contains(Jellyfin.Data.Enums.BaseItemKind.Episode)
            ? new BaseItem[] { new Episode { ParentIndexNumber = 1, IndexNumber = 1 } }
            : new BaseItem[] { new Series { Id = Guid.NewGuid(), Name = "Submitted Through Bridge", ProviderIds = new Dictionary<string, string> { ["Tvdb"] = "12345" } } });
        var app = new MediaForgeRequestApplicationService(client, store, new MediaAccessGrantStore(), new UserRateLimiter(), new JellyfinLibraryAvailabilityService(library), new JellixSelectionTokenStore(), () => config);
        return (app, store, handler);
    }

    private static AutomaticMediaRequest Selection(bool subscribe = false) => new()
    {
        Title = "Test", SeriesUrl = "https://example.invalid/series/search-result", Source = "source-a",
        MediaType = "series", Language = "German Dub", Provider = "VOE", SubscribeOnly = subscribe,
    };

    private static async Task LanguageAvailability(string root)
    {
        var env = Create(root, "workflow-languages");
        env.Handler.MixedLanguages = true;
        var selection = Selection(); selection.Language = "";
        var plan = await env.App.PlanAsync("one", selection, false, false, Token);
        Check(plan.Languages.Contains("German Dub") && plan.Languages.Contains("German Sub") && plan.Languages.Contains("English Sub"), "Partially available dub disappeared from the plan.");
        Check(plan.Providers["German Dub"].Contains("VOE"), "Nested providers or language representative was ignored.");
        Check(plan.LanguageCounts["German Dub"] == 1 && plan.LanguageCounts["German Sub"] == 2, "Per-language availability counts are wrong.");
        selection.Language = "German Dub";
        plan = await env.App.PlanAsync("one", selection, false, false, Token);
        Check(plan.MissingUrls.Count == 1 && plan.ToResponse().ExistingCount == 0 && plan.UnavailableCount == 1, "Unavailable dub counted as present or queued as a subtitle.");
        var result = await env.App.SubmitAutomaticAsync("one", "One", selection, false, Token);
        var approved = await env.App.ApproveAsync(result.Request!.Id, "Admin", Token);
        Check(approved.Status == "queued" && env.Handler.LastEpisodes.SequenceEqual(new[] { "https://example.invalid/episode/2" }), "Approval lost the selected dub language.");
        Check(approved.ExpectedEpisodes.SequenceEqual(new[] { new LibraryEpisodeKey(1, 2) }), "Library monitoring waits for unrequested subtitle-only episodes.");
        Check(env.Handler.AutosyncCalls == 1, "Partial dub selection did not subscribe for future episodes.");
        selection.Language = "French Dub";
        try
        {
            await env.App.SubmitAutomaticAsync("two", "Two", selection, false, Token);
            throw new InvalidOperationException("Unknown language was accepted as already available.");
        }
        catch (MediaForgeApplicationException error) when (error.StatusCode == HttpStatusCode.BadRequest) { }
        env.Handler.FlatProviders = true;
        selection.Language = "German Sub";
        plan = await env.App.PlanAsync("one", selection, false, false, Token);
        Check(plan.MissingUrls.Count == 2 && plan.Providers["German Sub"].Contains("VOE"), "Legacy flat provider mapping stopped working.");
    }

    private static async Task AutosyncDiagnostics(string root)
    {
        var env = Create(root, "workflow-autosync-diagnostics", true);
        env.Handler.AutosyncStatus = HttpStatusCode.BadGateway;
        env.Handler.AutosyncErrorBody = """{"code":"autosync_core_auth","error":"secret /private/path"}""";
        var result = await env.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(result.Request!.AutosyncError!.Contains("interne MediaForge-Anmeldung", StringComparison.Ordinal), "Autosync root cause hidden behind generic retry message.");
        Check(!result.Request.AutosyncError.Contains("secret", StringComparison.Ordinal), "Raw upstream error leaked.");
        env.Handler.AutosyncStatus = HttpStatusCode.Forbidden;
        env.Handler.AutosyncErrorBody = "<html>secret /private/path</html>";
        await env.App.EnsureAutosyncAsync(result.Request.Id, Token);
        var updated = await env.Store.GetAsync(result.Request.Id, Token);
        Check(updated!.AutosyncError!.Contains("queue:write", StringComparison.Ordinal) && updated.AutosyncError.Contains("403", StringComparison.Ordinal), "API scope failure was not classified.");
        Check(!updated.AutosyncError.Contains("secret", StringComparison.Ordinal), "Proxy error leaked.");
        env.Handler.AutosyncErrorBody = """{"code":"secret /private/path","error":"secret"}""";
        await env.App.EnsureAutosyncAsync(result.Request.Id, Token);
        Check(!(await env.Store.GetAsync(result.Request.Id, Token))!.AutosyncError!.Contains("secret", StringComparison.Ordinal), "Unknown connector code leaked.");
        env.Handler.AutosyncStatus = HttpStatusCode.OK;
        env.Handler.AutosyncErrorBody = null;
        await env.App.EnsureAutosyncAsync(result.Request.Id, Token);
        Check(env.Handler.DownloadCalls == 1 && (await env.Store.GetAsync(result.Request.Id, Token))!.AutosyncJobId == 7, "Diagnostic retries caused duplicate download.");
    }

    private static async Task AutosyncAndRules(string root)
    {
        var env = Create(root, "workflow-autosync");
        var result = await env.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(env.Handler.DownloadCalls == 0 && env.Handler.AutosyncCalls == 0, "Pending requests caused upstream writes.");
        var approved = await env.App.ApproveAsync(result.Request!.Id, "Admin", Token);
        Check(approved.Status == "queued" && approved.AutosyncJobId == 7, "Approval did not queue and subscribe.");
        Check(env.Handler.LastOperationId == approved.OperationId && approved.OperationId.Length == 32, "Persisted receipt ID was not sent to the new module.");
        await env.App.ApproveAsync(approved.Id, "Admin", Token);
        await env.App.EnsureAutosyncAsync(approved.Id, Token);
        Check(env.Handler.DownloadCalls == 1 && env.Handler.AutosyncCalls == 1, "Repeated approval duplicated an operation.");

        var retry = Create(root, "workflow-autosync-retry", true);
        retry.Handler.AutosyncStatus = HttpStatusCode.ServiceUnavailable;
        result = await retry.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(result.Request?.Status == "queued" && result.Request.AutosyncStatus == "retry" && result.Request.MediaForgeQueueId == 42, "Autosync failure corrupted confirmed queue state.");
        retry.Handler.AutosyncStatus = HttpStatusCode.OK;
        await retry.App.EnsureAutosyncAsync(result.Request!.Id, Token);
        Check(retry.Handler.DownloadCalls == 1 && (await retry.Store.GetAsync(result.Request.Id, Token))?.AutosyncJobId == 7, "Autosync recovery requeued a download.");

        var rules = Create(root, "workflow-rules", true);
        await rules.Store.SetRuleAsync("one", new UserRequestRule { ApprovalMode = "manual", AllowSubscriptions = false }, "Admin", Token);
        result = await rules.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(result.Request?.Status == "pending", "Manual user rule did not override global automatic mode.");
        approved = await rules.App.ApproveAsync(result.Request!.Id, "Admin", Token);
        Check(!approved.AutosyncRequested && rules.Handler.AutosyncCalls == 0 && rules.Handler.DownloadCalls == 1, "Disabled subscription rule blocked download or created an abo.");

        var movie = Create(root, "workflow-movie", true);
        movie.Handler.IsMovie = true;
        var selection = Selection(); selection.MediaType = "movie";
        result = await movie.App.SubmitAutomaticAsync("one", "One", selection, false, Token);
        Check(result.Request?.Status == "queued" && movie.Handler.DownloadCalls == 1 && movie.Handler.AutosyncCalls == 0, "Movie created an AutoSync job.");

        var legacy = Create(root, "workflow-legacy-pending");
        var stored = await legacy.Store.TryAddAsync("one", "One", new CreateMediaRequest { Title = "Legacy pending", SeriesUrl = selection.SeriesUrl, Source = "source-a", MediaType = "series", Language = "German Dub", Provider = "VOE", Episodes = ["https://example.invalid/episode/1"] }, "pending", 10, Token);
        approved = await legacy.App.ApproveAsync(stored.Request!.Id, "Admin", Token);
        Check(approved.ModernWorkflow && approved.AutosyncJobId == 7, "A newly approved legacy pending request did not receive AutoSync.");
    }

    private static async Task CrashAndReconcile(string root)
    {
        var env = Create(root, "workflow-crash", true);
        env.Handler.LoseDownloadResponse = true;
        var result = await env.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(result.Request?.Status == "uncertain", "Lost response was not marked uncertain.");
        await env.App.ApproveAsync(result.Request!.Id, "Admin", Token);
        await env.App.ReconcileAsync(result.Request.Id, "Admin", false, Token);
        Check(env.Handler.DownloadCalls == 1, "Uncertain handoff was blindly repeated.");
        env.Handler.ConfirmOperation = true;
        var recovered = await env.App.ReconcileAsync(result.Request.Id, "Admin", false, Token);
        Check(recovered?.Status == "queued" && recovered.AutosyncJobId == 7 && env.Handler.DownloadCalls == 1, "Receipt recovery failed.");
    }

    private static async Task SharedSelections(string root)
    {
        var store = new RequestStore(Path.Combine(root, "workflow-sharing"));
        CreateMediaRequest Input(string language, params string[] episodes) => new() { Title = "Series", SeriesUrl = "https://example.invalid/series", Source = "source-a", Language = language, Provider = "VOE", Episodes = episodes.ToList() };
        var first = await store.TryAddAsync("a", "Alice", Input("de", "ep1", "ep2"), "pending", 10, Token);
        var second = await store.TryAddAsync("b", "Bob", Input("de", "ep2", "ep3"), "pending", 10, Token);
        Check(second.Request!.Episodes.SequenceEqual(new[] { "ep3" }) && second.Request.SharedRequestIds.Contains(first.Request!.Id), "Overlap was not split into shared and new episodes.");
        var duplicate = await store.TryAddAsync("a", "Alice", Input("de", "ep2", "ep1"), "pending", 10, Token);
        Check(duplicate.Duplicate is not null, "Episode order defeated duplicate detection.");
        var otherLanguage = await store.TryAddAsync("c", "Carol", Input("en", "ep1"), "pending", 10, Token);
        Check(otherLanguage.Request!.SharedRequestIds.Count == 0, "Different languages were incorrectly merged.");
        await store.TryWithdrawAsync(first.Request!.Id, "a", "Alice", Token);
        Check((await store.GetAsync(first.Request.Id, Token))?.Status == "pending" && (await store.ListForUserAsync("a", 100, Token)).Count == 0, "Withdrawal cancelled another user's shared selection.");

        var env = Create(root, "workflow-shared-approval", false);
        var pending = await env.App.SubmitAutomaticAsync("a", "Alice", Selection(), false, Token);
        await env.Store.SetRuleAsync("b", new UserRequestRule { ApprovalMode = "automatic" }, "Admin", Token);
        var follower = await env.App.SubmitAutomaticAsync("b", "Bob", Selection(), false, Token);
        Check(follower.Request?.Status == "shared" && env.Handler.DownloadCalls == 0 && env.Handler.AutosyncCalls == 0, "Following bypassed a pending approval.");
        await env.App.ApproveAsync(pending.Request!.Id, "Admin", Token);
        await env.App.SynchronizeAsync(false, Token);
        Check(env.Handler.DownloadCalls == 1 && (await env.Store.GetAsync(follower.Request!.Id, Token))?.MediaForgeQueueIds.Contains(42) == true, "Follower did not receive shared progress.");

        var manual = Create(root, "workflow-manual-group");
        var owner = await manual.App.SubmitAutomaticAsync("a", "Alice", Selection(), false, Token);
        var participant = await manual.App.SubmitAutomaticAsync("b", "Bob", Selection(), false, Token);
        var page = await manual.Store.AdminPageAsync(null, null, null, null, null, 1, 30, Token);
        Check(page.Items.Count == 1, "Fully shared requests were not grouped for admins.");
        await manual.App.ApproveAsync(owner.Request!.Id, "Admin", Token);
        Check((await manual.Store.GetAsync(participant.Request!.Id, Token))?.Status == "shared", "Group approval left its hidden participant pending.");
    }

    private static async Task CompleteSubscription(string root)
    {
        var env = Create(root, "workflow-complete", true, true);
        var result = await env.App.SubmitAutomaticAsync("one", "One", Selection(true), false, Token);
        Check(result.Request?.AutosyncJobId == 7 && env.Handler.DownloadCalls == 0, "Complete subscription queued existing content.");
    }

    private static async Task MigrationAndNotifications(string root)
    {
        var path = Path.Combine(root, "workflow-migration"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "requests.json"), "{\"nextId\":2,\"requests\":[{\"id\":1,\"userId\":\"old\",\"status\":\"completed\",\"mediaForgeQueueId\":42}]}");
        var store = new RequestStore(path);
        _ = new RequestStore(path); // migration is restart-safe before first save
        Check(File.Exists(Path.Combine(path, "requests.json.v1-backup")), "Migration backup missing.");
        var old = await store.GetAsync(1, Token);
        Check(old?.MediaForgeQueueId == 42 && !old.AutosyncRequested && !old.ModernWorkflow, "Migration changed historical requests.");
        Check((await store.NotificationsAsync("old", Token)).Items.Count == 0, "Migration sent historical notifications.");

        var env = Create(root, "workflow-notifications", true);
        var result = await env.App.SubmitAutomaticAsync("a", "Alice", Selection(), false, Token);
        var notifications = await env.Store.NotificationsAsync("a", Token);
        Check(notifications.Items.Count == 1, "Approval notification missing or duplicated.");
        await env.Store.MarkQueuedAsync(result.Request!.Id, 42, "Admin", Token);
        Check((await env.Store.NotificationsAsync("a", Token)).Items.Count == 1, "Repeated queue event duplicated notification.");
        await env.Store.UpdateNotificationsAsync("b", notifications.Items[0].Id, null, Token);
        Check((await env.Store.NotificationsAsync("a", Token)).Items[0].ReadUtc is null, "Another user marked a notification read.");
        var restarted = new RequestStore(Path.Combine(root, "workflow-notifications"));
        Check((await restarted.NotificationsAsync("a", Token)).Items.Count == 1, "Notifications did not survive restart.");
    }

    private static async Task BackgroundAndPagination(string root)
    {
        var env = Create(root, "workflow-background", true);
        var result = await env.App.SubmitAutomaticAsync("a", "Alice", Selection(), false, Token);
        env.Handler.ReportCompleted = true;
        await env.App.SynchronizeAsync(false, Token);
        Check((await env.Store.GetAsync(result.Request!.Id, Token))?.Status == "completed", "Background worker did not complete unopened request.");
        var page = await env.Store.AdminPageAsync("Submitted", "a", "completed", "source-a", null, 1, 1, Token);
        Check(page.Total == 1 && page.Items.Count == 1 && page.Downloading == 0, "Filtered admin page/counters incorrect.");
        Check((await env.Store.AdminPageAsync(null, null, null, null, null, 2, 1, Token)).Items.Count == 0, "Pagination repeated first page.");
    }

    private static async Task NewestRequestsFirst(string root)
    {
        var env = Create(root, "workflow-newest-first");
        var ids = new List<long>();
        var epoch = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
        var statuses = new[] { RequestStatuses.Completed, RequestStatuses.Failed, RequestStatuses.Pending, RequestStatuses.Queued };
        var minutes = new[] { 3, 0, 1, 3 };
        for (var index = 0; index < statuses.Length; index++)
        {
            var added = await env.Store.TryAddAsync("one", "One", new CreateMediaRequest
            {
                Title = "Ordered " + index, SeriesUrl = "https://example.invalid/order/" + index, Source = "source-a",
                Language = "German Dub", Provider = "VOE", Episodes = ["https://example.invalid/order/episode/" + index],
            }, RequestStatuses.Pending, 10, Token);
            var id = added.Request!.Id; ids.Add(id);
            await env.Store.UpdateWorkflowAsync(id, row => { row.CreatedUtc = epoch.AddMinutes(minutes[index]); row.Status = statuses[index]; }, Token);
        }
        var expected = new[] { ids[3], ids[0], ids[2], ids[1] };
        Check((await env.Store.ListForUserAsync("one", 10, Token)).Select(r => r.Id).SequenceEqual(expected), "Personal requests are not newest first with stable ties.");
        Check((await env.Store.ListAllAsync(2, Token)).Select(r => r.Id).SequenceEqual(expected.Take(2)), "Legacy request limit was applied before date sorting.");
        var first = await env.Store.AdminPageAsync(null, null, null, null, null, 1, 2, Token);
        var second = await env.Store.AdminPageAsync(null, null, null, null, null, 2, 2, Token);
        Check(first.Items.Concat(second.Items).Select(r => r.Id).SequenceEqual(expected), "Admin pagination prioritizes status or IDs over request date.");
        Check((await env.Store.ListForUserAsync("other", 10, Token)).Count == 0, "Date sorting bypassed user isolation.");
    }

    private static async Task ResetAutomaticRules(string root)
    {
        var env = Create(root, "workflow-reset-rules");
        await env.Store.SetRuleAsync("one", new UserRequestRule { ApprovalMode = "automatic", MaxOpenRequests = 7, AllowSubscriptions = false }, "Admin", Token);
        await env.Store.SetRuleAsync("other", new UserRequestRule { ApprovalMode = "automatic" }, "Admin", Token);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => env.Store.ResetAutomaticApprovalAsync("one", "Admin", Token)));
        var restarted = new RequestStore(Path.Combine(root, "workflow-reset-rules"));
        var rule = await restarted.GetRuleAsync("one", Token);
        Check(rule.ApprovalMode == "inherit" && rule.MaxOpenRequests == 7 && !rule.AllowSubscriptions, "Reset lost unrelated rules or did not persist.");
        Check((await restarted.GetRuleAsync("other", Token)).ApprovalMode == "automatic", "Reset changed another user.");
        var pending = await env.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        Check(pending.Request?.Status == "pending", "Reset did not restore global manual approval.");
        await env.Store.SetRuleAsync("one", new UserRequestRule { ApprovalMode = "manual", MaxOpenRequests = 4 }, "Admin", Token);
        Check((await env.Store.ResetAutomaticApprovalAsync("one", "Admin", Token)).ApprovalMode == "manual", "Stale reset overwrote newer manual approval.");
        var automatic = Create(root, "workflow-reset-global-automatic", true);
        await automatic.Store.SetRuleAsync("one", new UserRequestRule { ApprovalMode = "automatic" }, "Admin", Token);
        await automatic.Store.ResetAutomaticApprovalAsync("one", "Admin", Token);
        Check((await automatic.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token)).Request?.Status == "queued", "Reset did not inherit global automatic approval.");
    }

    private static void Authorization()
    {
        var type = typeof(WorkflowController);
        Check(type.IsDefined(typeof(AuthorizeAttribute), true), "Workflow controller is not authenticated.");
        foreach (var name in new[] { "Overview", "PendingCount", "Batch", "Recovery", "Users", "Rule", "ResetAutomaticRule", "Diagnostics" })
            Check(type.GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(a => a.Policy == Policies.RequiresElevation), "Missing admin authorization on " + name);
    }

    private static async Task PendingBadgeCount(string root)
    {
        var env = Create(root, "workflow-pending-badge");
        Check(await env.Store.PendingApprovalCountAsync(Token) == 0, "Empty store shows an admin badge.");
        var first = await env.App.SubmitAutomaticAsync("one", "One", Selection(), false, Token);
        await env.App.SubmitAutomaticAsync("two", "Two", Selection(), false, Token);
        Check(await env.Store.PendingApprovalCountAsync(Token) == 1, "Shared pending request was counted twice.");
        var additional = await env.Store.TryAddAsync("three", "Three", new CreateMediaRequest
        {
            Title = "Additional", SeriesUrl = "https://example.invalid/other", Source = "source-a",
            Language = "German Dub", Provider = "VOE", Episodes = ["https://example.invalid/other/episode/1"],
        }, RequestStatuses.Pending, 10, Token);
        Check(await env.Store.PendingApprovalCountAsync(Token) == 2, "Additional approval missing from badge.");
        await env.Store.MarkQueuedAsync(additional.Request!.Id, 43, "Admin", Token);
        Check(await env.Store.PendingApprovalCountAsync(Token) == 1, "Queued download occupies pending badge.");
        await env.App.ApproveAsync(first.Request!.Id, "Admin", Token);
        await env.App.SynchronizeAsync(false, Token);
        Check(await env.Store.PendingApprovalCountAsync(Token) == 0, "Approved requests left a stale badge.");
    }

    private static async Task BatchingAndDigests(string root)
    {
        var env = Create(root, "workflow-batching");
        for (var index = 1; index <= 205; index++)
        {
            var result = await env.Store.TryAddAsync("u" + index, "User", new CreateMediaRequest
            {
                Title = "Series " + index, SeriesUrl = "https://example.invalid/series/" + index, Source = "source-a",
                Language = "de", Provider = "VOE", Episodes = ["https://example.invalid/episode/" + index],
            }, "pending", 10, Token);
            await env.Store.MarkQueuedAsync(result.Request!.Id, index, "Admin", Token);
        }
        env.Handler.ReportCompleted = true;
        await env.App.SynchronizeAsync(false, Token);
        Check(env.Handler.ProgressBatchSizes.SequenceEqual(new[] { 200, 5 }), "Background progress exceeded the module's 200-ID limit.");
        Check((await env.Store.SnapshotAsync(Token)).All(r => r.Status == "completed"), "Background batches lost requests.");

        var digest = Create(root, "workflow-digest");
        var result2 = await digest.App.SubmitAutomaticAsync("a", "Alice", Selection(), false, Token);
        var id = result2.Request!.Id;
        await digest.Store.UpdateWorkflowAsync(id, row =>
        {
            row.Status = "available"; row.AutosyncJobId = 7; row.AutosyncStatus = "ready";
            row.SeenEpisodes = [new(1, 1)]; row.ExpectedEpisodes = [new(1, 1)]; row.LastDigestUtc = DateTime.UtcNow.AddDays(-1);
        }, Token);
        var episodes = new HashSet<LibraryEpisodeKey> { new(1, 1), new(1, 2), new(1, 3) };
        await digest.Store.ObserveLibraryAsync(id, episodes, true, Token);
        await digest.Store.ObserveLibraryAsync(id, episodes, true, Token);
        var notices = (await digest.Store.NotificationsAsync("a", Token)).Items.Where(n => n.Key.StartsWith("episodes:", StringComparison.Ordinal)).ToArray();
        Check(notices.Length == 1 && notices[0].Episodes.Count == 2, "Daily episode digest was not deduplicated.");
        await digest.Store.UpdateNotificationsAsync("a", null, new NotificationPreferences { NewEpisodes = "off" }, Token);
        episodes.Add(new(1, 4));
        await digest.Store.ObserveLibraryAsync(id, episodes, true, Token);
        Check((await digest.Store.NotificationsAsync("a", Token)).Items.Count(n => n.Key.StartsWith("episodes:", StringComparison.Ordinal)) == 1, "Disabled episode notifications were sent.");
    }
}
