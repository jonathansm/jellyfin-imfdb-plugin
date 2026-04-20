# Jellyfin IMFDB

Jellyfin IMFDB is a Jellyfin server plugin that looks up movies and series in IMFDB data and adds a firearm card row to Jellyfin Web by using the File Transformation plugin.

## What It Does

- Adds a server API endpoint at `/Imfdb/Lookup?itemId=<jellyfin-item-guid>` for testing and integrations.
- Uses the Jellyfin library item title, year, and IMDb provider id when available.
- Searches IMFDB/IMFDB Browser for matching movie or TV entries.
- Groups firearm appearances into card data by firearm name.
- Registers a File Transformation for Jellyfin Web `index.html`.
- Injects the bundled `/Imfdb/ClientScript` script from the server response.
- Adds a dashboard configuration page for lookup and UI injection options.

## File Transformation

This plugin relies on File Transformation to alter the Jellyfin Web HTML served by the server. That avoids browser extensions and avoids modifying Jellyfin Web files on disk, but it does still inject a small client script into Jellyfin Web so a new actor-style row can be rendered.

Install File Transformation from:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

After installing both plugins, restart Jellyfin and hard refresh Jellyfin Web. If File Transformation is not installed or is not loaded, the `/Imfdb/Lookup` API will still work, but the firearm row will not appear.

## Build

This project targets Jellyfin 10.11.x and .NET 9:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Copy the publish output from `Jellyfin.Plugin.Imfdb/bin/Release/net9.0/publish/` into a Jellyfin plugin folder, then restart Jellyfin.

## Development Status

This is an initial implementation. IMFDB does not provide a stable first-party JSON API for every page, so the plugin uses the public IMFDB Browser pages where possible and falls back to MediaWiki search/parse behavior. Scraping can break if those pages change.
