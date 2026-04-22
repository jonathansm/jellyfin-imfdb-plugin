# Jellyfin IMFDB

> Disclaimer: this project is vibecoded. It should be treated as an experimental community plugin rather than a polished official Jellyfin integration.

Jellyfin IMFDB is a Jellyfin server plugin that looks up movies and series in IMFDB data and adds a firearm card row to Jellyfin Web by using the File Transformation plugin.

## Features

- Adds firearm cards to Jellyfin movie and series detail pages.
- Shows firearm names, images where available, and basic firearm details.
- Links each firearm back to the matching IMFDB title section when possible.
- Uses Jellyfin item title, year, and IMDb provider id when available.
- Adds a diagnostic API endpoint at `/Imfdb/Status`.
- Adds a lookup API endpoint at `/Imfdb/Lookup?itemId=<jellyfin-item-guid>`.
- Adds a dashboard configuration page for lookup and UI injection options.

## Important Limitations

This plugin depends on web scraping and unofficial public pages. IMFDB does not provide a stable first-party JSON API for all of the data this plugin needs. The plugin currently reads from IMFDB Browser pages and uses best-effort enrichment from public sources such as Wikipedia for firearm images and descriptions.

That means this plugin can break if:

- IMFDB Browser changes its HTML structure.
- IMFDB changes page names or section anchors.
- The File Transformation plugin changes its registration API.
- Jellyfin Web changes the DOM structure around movie, series, or actor sections.
- Public detail/image sources change, rate-limit, or stop returning matching data.

If a title has no IMFDB entry, the firearms row should stay hidden. If an entry exists but no firearm image/details can be found, the card may still appear with limited information.

## Requirements

- Jellyfin 10.11.x.
- .NET 9 compatible Jellyfin plugin runtime.
- Jellyfin Web hosted by the Jellyfin server.
- [File Transformation plugin](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation).

Install File Transformation from its Jellyfin plugin repository:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

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

Open this URL on your Jellyfin server:

```text
https://YOUR-JELLYFIN-SERVER/Imfdb/Status
```

Expected fields:

- `PluginEnabled`: IMFDB lookup is enabled.
- `WebUiInjectionEnabled`: Jellyfin Web injection is enabled.
- `FileTransformationRegistered`: the plugin successfully registered with File Transformation.
- `FileTransformationStatus`: the most recent registration status.

If the row does not appear:

- Confirm File Transformation and IMFDB are both installed and enabled.
- Fully restart Jellyfin, not just the web page.
- Confirm `/Imfdb/Status` reports `FileTransformationRegistered: true`.
- Open browser developer tools and check for `/Imfdb/ClientScript`.
- Test with a known IMFDB-heavy movie such as `John Wick`, `Die Hard`, or `The Matrix`.

## Development

Build the plugin:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Package a release zip:

```bash
./scripts/package-plugin.sh 0.1.1.0
```

The package script creates:

```text
artifacts/jellyfin-plugin-imfdb_0.1.1.0.zip
artifacts/jellyfin-plugin-imfdb_0.1.1.0.zip.md5
```

## License

GPL-3.0-only.
