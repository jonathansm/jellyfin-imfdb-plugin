# Jellyfin IMFDB

> Disclaimer: this project is vibecoded. It should be treated as an experimental community plugin rather than a polished official Jellyfin integration.

Jellyfin IMFDB is a Jellyfin server plugin that looks up movies and series in IMFDB data and adds a firearm card row to Jellyfin Web by using the File Transformation plugin.

## Version 0.3.0.0

This version targets **Jellyfin 12 and .NET 10**. It updates authentication and web integration, enforces library permissions on lookup and image requests, and fixes cache handling and request retries. See [CHANGELOG.md](CHANGELOG.md) for details.

Use a Jellyfin 12-compatible File Transformation build. Older IMFDB releases remain available for Jellyfin 10.11. Version 0.3.0.0 has passed automated tests; live installation checks are listed in [REVIEW.md](REVIEW.md).

## Features

- Adds firearm cards to Jellyfin movie and series detail pages.
- Shows firearm names, images where available, and basic firearm details.
- Links each firearm back to the matching IMFDB title section when possible.
- Uses Jellyfin item title and year when available.
- Adds a diagnostic API endpoint at `/Imfdb/Status`.
- Adds a lookup API endpoint at `/Imfdb/Lookup?itemId=<jellyfin-item-guid>`.
- Adds a dashboard configuration page for lookup and UI injection options.
- Optionally caches IMFDB lookup results and images in plugin-managed storage.

## Important Limitations

This plugin depends on unofficial public wiki data. IMFDB does not provide a stable first-party JSON API for all of the data this plugin needs. The plugin currently searches IMFDB's MediaWiki API to find the matching title page, then reads firearm cards, section text, captions, and images from IMFDB wiki source data when the public endpoint allows it.

That means this plugin can break if:

- IMFDB wiki search results change shape or ranking behavior.
- IMFDB wiki source responses change shape, become unavailable, or block automated requests.
- IMFDB changes page names or section anchors.
- The File Transformation plugin changes its registration API.
- Jellyfin Web changes the DOM structure around movie, series, or actor sections.
- IMFDB detail/image URLs change, rate-limit, or stop returning matching data.

If a title has no IMFDB entry, the firearms row should stay hidden. If an entry exists but no firearm image/details can be found, the card may still appear with limited information.

## Caching

IMFDB caching is enabled by default. On first view, the plugin looks up IMFDB results and writes metadata and images under the plugin data folder before returning. Image downloads have a 20-second timeout and run up to four at a time for each lookup. Later views load cached cards first, then refresh from IMFDB only after the configured refresh interval has elapsed.

Movie and series metadata is stored under `cache/metadata/movies` and `cache/metadata/tv`, respectively. Images are stored separately under `cache/images` by image URL hash, so the same IMFDB image reused by multiple titles is downloaded once.

The dashboard setting `Refresh cached results after` controls how often cached results are refreshed. Use `0` to refresh every time a cached result is served.

## Requirements

- Jellyfin 12.0 or later in the 12.x series (this build targets the 12.0 API).
- .NET 10 compatible Jellyfin plugin runtime.
- Jellyfin Web hosted by the Jellyfin server.
- A Jellyfin 12-compatible [File Transformation plugin](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) build.

Install File Transformation from its Jellyfin plugin repository:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

Version 0.3.0.0 targets Jellyfin 12; older published versions remain for Jellyfin 10.11. The repository catalog will offer 0.3.0.0 after it is released. For local testing, build the package below and extract its contents into a new IMFDB directory under your Jellyfin plugins directory while the server is stopped. Replace the old IMFDB installation, start the server, and hard refresh the browser.

## Installation

Add this plugin repository in Jellyfin:

```text
https://jonathansm.github.io/jellyfin-imfdb-plugin/manifest.json
```

In Jellyfin:

1. Go to Dashboard -> Plugins -> Repositories.
2. Add the File Transformation repository.
3. Add the IMFDB repository.
4. Go to Catalog.
5. Install File Transformation.
6. Install IMFDB.
7. Restart Jellyfin.
8. Hard refresh Jellyfin Web.

If File Transformation is not installed or is not loaded, the `/Imfdb/Lookup` API may still work, but the firearm row will not appear in Jellyfin Web.

## Troubleshooting

The status endpoint requires administrator authentication. From browser developer tools while signed in as an administrator, run:

```javascript
fetch(ApiClient.getUrl("Imfdb/Status"), {
  headers: { Authorization: `MediaBrowser Token="${ApiClient.accessToken()}"` }
}).then(response => response.json()).then(console.log);
```

Endpoint URL:

```text
https://YOUR-JELLYFIN-SERVER/Imfdb/Status
```

Expected fields:

- `PluginEnabled`: IMFDB lookup is enabled.
- `WebUiInjectionEnabled`: Jellyfin Web injection is enabled.
- `CachingEnabled`: local IMFDB result and image caching is enabled.
- `CacheRefreshIntervalHours`: cached-result refresh interval.
- `FileTransformationRegistered`: the plugin successfully registered with File Transformation.
- `FileTransformationStatus`: the most recent registration status.

If the row does not appear:

- Confirm File Transformation and IMFDB are both installed and enabled.
- Fully restart Jellyfin, not just the web page.
- Confirm `/Imfdb/Status` reports `FileTransformationRegistered: true`.
- Open browser developer tools and check for `/Imfdb/ClientScript`.
- Test with a known IMFDB-heavy movie such as `John Wick`, `Die Hard`, or `The Matrix`.

## Development

Run regression tests (.NET 10 SDK and Node.js 22 or later):

```bash
dotnet test Jellyfin.Plugin.Imfdb.sln -c Release
node --test tests/*.test.cjs
```

Build the plugin:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Package a release zip:

```bash
./scripts/package-plugin.sh 0.3.0.0
```

The package script creates:

```text
artifacts/jellyfin-plugin-imfdb_0.3.0.0.zip
artifacts/jellyfin-plugin-imfdb_0.3.0.0.zip.md5
```

## Validation

See [REVIEW.md](REVIEW.md) for review findings, fixes, and the remaining live-server checks. The web row uses the server-hosted Jellyfin Web detail page; independent native clients do not run this script. Lookup and image endpoints enforce the requesting user's library visibility. Only movie and series items are searched.

## License

GPL-3.0-only.
