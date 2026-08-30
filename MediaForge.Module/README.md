# MediaForge Module: Jellyfin Connector

Version 0.5.1 separates this fork from the official connector using the
`marshmello_jellyfin_connector` identity, folder and blueprint, and the
`/api/v1/marshmello-connector/` API. Install **Jellyfin Connector - Marshmello**
as a separate module and update the Jellyfin plugin to 0.5.1 as well. The official
module can remain installed; it serves different routes. Existing AutoSync
registration and durable download handoff receipts are retained. Back up
`jellyfin-connector-receipts.sqlite3` in the MediaForge configuration directory
alongside the normal MediaForge database. Existing API scopes remain sufficient.
See [the full workflow and recovery guide](../docs/WORKFLOW.md).

This companion module adds API-key-protected endpoints to MediaForge for
searching, resolving titles, seasons, and episodes, and queueing downloads. It
uses the same handlers as the MediaForge Web UI internally, so the sources,
providers, and download settings enabled in MediaForge automatically apply.

Adult sources remain subject to MediaForge's central age gate. API-key clients
cannot opt into adult results. Poster references use MediaForge's `/api/img`
proxy; the connector keeps a scoped compatibility route for MediaForge 1.5.

## Installation

For installation through **Additional repositories / Weitere Repositories**,
upload the root `module-store` folder with the project and use your own URL:

```text
https://raw.githubusercontent.com/Marshmello0w/Mediaforge-Jellyfin-Connector/main/module-store/index.json
```

Allow unverified modules only if you trust this source, refresh the store,
install/update **Jellyfin Connector – Marshmello**, then restart MediaForge. The package is
unsigned; its SHA-256 checksum does not confer a verified signature. Both
`index.json` and `index-all.json` are supplied. See the [store guide](../module-store/README.md).
The following manual procedure remains available without store support:

1. Copy the `marshmello_jellyfin_connector` directory to
   `~/.mediaforge/thirdparties/marshmello_jellyfin_connector`.
2. Restart MediaForge.
3. In MediaForge, open **Module Manager > Module Settings** and confirm that
   **Jellyfin Connector – Marshmello** is enabled.
4. Under **Settings > API**, create a key with these scopes: `status:read`,
   `library:read`, `queue:read`, and `queue:write`.
5. Enter the MediaForge URL and the key, which is displayed only once, in the
   Jellyfin plugin settings.

The connector intentionally has no separate page of its own. Its only
module-specific setting is the **Enable Jellyfin Connector – Marshmello** toggle under
**Module Manager > Module Settings**. API keys are managed centrally by
MediaForge under **Settings > API** and are not stored in this module.

If the module card is missing, verify the exact directory layout:

```text
~/.mediaforge/thirdparties/marshmello_jellyfin_connector/__init__.py
~/.mediaforge/thirdparties/marshmello_jellyfin_connector/routes.py
```

There must not be a second nested `marshmello_jellyfin_connector` directory.
After correcting a manual installation, restart MediaForge. MediaForge 1.6 can
also apply the registration safely through the Module Manager's refresh
function; MediaForge 1.5 requires a restart. The MediaForge log will report an
import or compatibility error if registration still fails.

After saving, the key is stored in encrypted form by the Jellyfin plugin and is
never returned to a browser. The module validates every submitted media URL
against MediaForge's own provider registry, limits field lengths, and accepts
only the expected JSON fields when queueing a download.

The companion Jellyfin plugin checks existing films and episodes directly in
the Jellyfin library. This MediaForge module therefore performs no filesystem
or provider-page `downloaded` correction of its own.
