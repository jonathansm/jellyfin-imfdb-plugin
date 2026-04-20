# Jellyfin IMFDB

Jellyfin IMFDB is a Jellyfin server plugin that looks up movies and series in IMFDB data and writes firearm information into native Jellyfin metadata fields.

## What It Does

- Adds a server API endpoint at `/Imfdb/Lookup?itemId=<jellyfin-item-guid>` for testing and integrations.
- Uses the Jellyfin library item title, year, and IMDb provider id when available.
- Searches IMFDB/IMFDB Browser for matching movie or TV entries.
- Enriches movie and series metadata during metadata refresh/library scans.
- Appends a marked, replaceable `IMFDB Firearms` section to the existing item overview.
- Optionally adds searchable firearm tags such as `imfdb:Beretta 92F`.
- Adds a dashboard configuration page for enrichment options.

## UI Tradeoff

This version avoids Jellyfin Web script injection. Because Jellyfin server plugins cannot add a brand-new actor-style card row to the existing detail page by themselves, firearm data appears in existing Jellyfin fields instead. The most visible default is the item overview.

To populate data, refresh metadata for movies or series after installing the plugin. The plugin can also be configured from Dashboard -> Plugins -> IMFDB.

## Build

This project targets Jellyfin 10.11.x and .NET 9:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Copy the publish output from `Jellyfin.Plugin.Imfdb/bin/Release/net9.0/publish/` into a Jellyfin plugin folder, then restart Jellyfin.

## Development Status

This is an initial implementation. IMFDB does not provide a stable first-party JSON API for every page, so the plugin uses the public IMFDB Browser pages where possible and falls back to MediaWiki search/parse behavior. Scraping can break if those pages change.
