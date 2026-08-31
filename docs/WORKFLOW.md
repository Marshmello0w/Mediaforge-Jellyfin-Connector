# Requests and AutoSync (0.5.1)

## Installation and upgrade

Version 0.5.1 introduces a separate MediaForge identity and folder,
`marshmello_jellyfin_connector`, shown as **Jellyfin Connector - Marshmello**.
The official `mediaforge_jellyfin_connector` ID otherwise takes precedence in
MediaForge's store merge regardless of our higher version. Install the new
entry separately, restart MediaForge, then update the Jellyfin plugin to 0.5.1
and restart Jellyfin. The new plugin uses `/api/v1/marshmello-connector/` only;
it does not silently fall back to the official connector. Both MediaForge
modules can coexist because their blueprints, scopes, setting namespaces and
route URLs are distinct. Jellyfin's own API/Jellix protocol and plugin identity
remain unchanged, preserving request data and settings. Keep the existing
receipt database to reconcile requests from before the namespace change.

Install **both** release packages. Update the MediaForge companion module
first, restart MediaForge, then update the Jellyfin plugin and restart Jellyfin.
Jellyfin's plugin updater cannot install the companion module. The existing
`status:read`, `library:read`, `queue:read`, and `queue:write` scopes suffice.

Before upgrading, back up the Jellyfin plugin data directory and the MediaForge
configuration directory. The request store upgrades to schema 2 and creates
`requests.json.v1-backup` before changing a legacy document. Existing IDs and
queue IDs are preserved. Historical requests do **not** receive subscriptions
or historical notifications. Pending requests approved after the upgrade use
the new workflow. Legacy active downloads are monitored without backfilling
AutoSync.

The MediaForge configuration directory gains
`jellyfin-connector-receipts.sqlite3`, containing operation fingerprints and
queue confirmations. Include this file in backups. Never delete it to retry a
download: losing a receipt removes evidence needed to avoid duplicate writes.

To roll back, stop Jellyfin, restore the pre-upgrade request store and the old
plugin package, and restart. AutoSync jobs already created in MediaForge remain
in MediaForge; a rollback does not delete them. Preserve the receipt database.

## Approval and AutoSync

New approved series and automatic approvals download the missing content and
then create/reuse an AutoSync job. Movies only download once. A complete series
can be submitted using **Zukünftige Folgen abonnieren**, through the same
approval rules and without an initial download.

New AutoSync jobs use the selected language/provider and the source's default
download path. The connector does not set episode filters. Existing jobs are
left unchanged, including disabled/held state, filters, language, provider,
and paths. The UI reports inherited restrictions. Management of the actual
AutoSync schedule, paths and filters remains in MediaForge. The connector does
not recreate confirmed subscriptions after an administrator deletes them.

The initial core AutoSync scan uses `queue_downloads=False`. Later scans use
MediaForge's configured schedule and missing-file rules. Upscaling applies to
the initial connector download; future AutoSync processing follows MediaForge's
own supported settings (the AutoSync job API has no per-job upscale field).

An AutoSync error cannot undo a confirmed download. Pending subscriptions are
durable and retry after 1, 5, 15, then every 60 minutes. The **Nur Autosync erneut
versuchen** action retries only the subscription. Source and per-user rules are
checked again before writing.

## Handoff recovery

The connector commits a durable operation reservation before invoking
MediaForge's core queue handler. Repeating an operation returns its confirmed
queue ID rather than issuing another download. Conflicting payloads are rejected.

A lost response or interrupted handoff becomes **Übergabe unklar**. Recovery
checks the receipt and, when necessary, looks for one exact matching queue item
created after the reservation's queue watermark. Multiple candidates, missing
items, or changed metadata do not count as confirmation. No uncertain handoff
is automatically resent.

Administrators can use **Übergabe abgleichen**. If confirmation remains
impossible, **Erneut senden…** requires explicit confirmation of the duplicate
risk and creates a new operation ID. Receipts and audit history are retained.
An older module lacking receipt support can still accept initial downloads;
uncertain writes on such modules may require manual reconciliation.

## Shared requests and rules

Matching uses source URL, source, language, provider, upscale choice, and episode
sets. Episode order does not affect matching. Overlapping episodes link to the
existing operation; only the uncovered remainder gets another queue operation.
Titles alone never merge different sources or remakes. Users see their own
participation; participant identities are exposed only in the admin overview.

Following a pending request does not approve that request. An administrator's
group approval includes fully shared pending participations. A user can
withdraw their pending participation without cancelling other participants or
running downloads. The underlying pending operation remains if others depend
on it. Completed downloads free request slots; permanent subscriptions do not
occupy open-download slots.

Admin **Benutzerregeln** overrides the global approval mode and maximum open
requests per user, and can disable new subscriptions for a user. Defaults inherit
global settings and allow subscriptions. Rules apply equally to HTTP and Jellix.

## Progress, library availability and notifications

A hosted worker checks active queues every 30 seconds, in batches of at most
200 IDs. It runs without any page being open. Library add/update events request
a new availability check, with a five-minute fallback. Download completion and
actual library availability remain separate states. Links and availability
notifications respect the requesting user's Jellyfin visibility.

The plugin's **Mitteilungen** tab contains persistent, deduplicated notifications
for approvals, rejections and availability. New episodes default to daily
digests by series; users can select immediate updates or disable categories.
Digest scheduling uses UTC day boundaries, and flushes on the next background
library check. Read messages expire 90 days after creation, unread ones after
180 days. No external messaging service is contacted.

Admins can search/filter the overview, page through the full history, act on up
to 50 requests per batch, enter rejection reasons, inspect progress and use
separate recovery actions. Partial batch failures are returned per request.
**Diagnose** shows connection health, versions, capabilities and scope checks,
never credentials, internal paths, or raw upstream errors.

## API compatibility

Existing routes and Jellix protocol-v1 fields remain supported. Personal request
records add AutoSync state, history, participation/operation references and
`mediaForgeQueueIds`; `mediaForgeQueueId` remains available to older consumers.
New request creation is still planned and authorized server-side; the browser
cannot choose arbitrary episode URLs or another user's operation.

New Jellyfin routes under `/MediaForgeRequests`:

| Method | Route | Access |
|---|---|---|
| POST | `Requests/Matching`, `Requests/Participation` | Signed-in user, granted selection |
| GET | `Requests/{id}/Library` | Request owner, visible library item |
| GET | `Notifications` | Own notifications |
| POST | `Notifications/Read` | Own notification ID or `all` |
| PUT | `Notifications/Preferences` | Own preferences |
| GET | `Admin/Overview`, `Admin/Users`, `Admin/Diagnostics` | Jellyfin administrator |
| POST | `Admin/Batch`, `Admin/Requests/{id}/Recovery` | Jellyfin administrator |
| PUT | `Admin/Users/{userId}/Rule` | Jellyfin administrator |

The overview accepts `query`, `userId`, `status`, `source`, `since`, `page`, and
`pageSize` (1–100). Batch bodies contain `ids`, `action` (`approve`/`reject`) and
optional `reason`. Recovery actions are `autosync`, `missing`, or `reconcile`;
resending requires `confirmPossibleDuplicate: true` with `reconcile`.

New MediaForge routes:

| Method | Route | Scope |
|---|---|---|
| POST | `/api/v1/marshmello-connector/autosync` | `queue:write` |
| GET | `/api/v1/marshmello-connector/operations/{operation_id}` | `queue:read` |

Downloads optionally accept a 32-character hexadecimal `operation_id`. The
health response advertises `autosync` and `download-receipts` capabilities and
the four required permission checks. AutoSync responses contain only
`job_id`, `created`, `enabled`, `on_hold`, and `filtered`.

## Validation

Run the existing .NET security executable, which now includes workflow tests,
and all Python tests:

```powershell
dotnet restore Tests/Connector.SecurityTests/Connector.SecurityTests.csproj --locked-mode
dotnet run --project Tests/Connector.SecurityTests/Connector.SecurityTests.csproj -c Release
python -m unittest discover -s Tests -p "test_*.py"
ruff check MediaForge.Module Tests/*.py
node --check Jellyfin.Plugin.MediaForge/Web/requests.js
```

The suite exercises scoped routes on modern and legacy API-key interfaces,
concurrent subscriptions and receipt reservations, crash recovery, sharing,
user rules, migration, notification isolation/digests and 200-ID batching.
An actual Jellyfin/MediaForge installation is still needed for deployment
acceptance: verify source-specific provider resolution, download directories,
library naming and the arrival of a new episode under the real AutoSync worker.

## Sprachverfügbarkeit (0.5.2)

Die Planung bildet die Vereinigung der ausgewiesenen Episodensprachen und hält
Dub/Sub getrennt. `language_counts` ergänzt die Planantwort um die Anzahl fehlender
Folgen je Sprache. Mit ausgewählter Sprache enthält `missing_count` nur passende
Folgen; `unavailable_count` zählt fehlende Folgen ohne diese Sprache separat.
Diese zählen ausdrücklich nicht zu `existing_count`. Ohne passende Folgen wird
eine Downloadanfrage abgelehnt, nicht als bereits vorhanden bestätigt.
MediaForge bleibt für tatsächliche Hoster- und Downloadverfügbarkeit maßgeblich.
Die Bibliotheksprüfung erkennt weiterhin Episoden nach Identität und Nummer,
nicht einzelne Audiospuren vorhandener Dateien.
