# Jellyfin IMFDB

Jellyfin IMFDB is a Jellyfin server plugin plus a small Jellyfin Web companion script. It looks up the current movie or series in IMFDB data and adds a firearms row to the item detail page, modeled after Jellyfin's actor/person cards.

## What It Does

- Adds a server API endpoint at `/Imfdb/Lookup?itemId=<jellyfin-item-guid>`.
- Uses the Jellyfin library item title, year, and IMDb provider id when available.
- Searches IMFDB/IMFDB Browser for matching movie or TV entries.
- Groups firearm appearances into cards by firearm name.
- Bundles a client script at `/Imfdb/ClientScript` that injects the row into Jellyfin Web item pages.
- Adds a dashboard configuration page with the script URL and quick test instructions.

## Important UI Note

Jellyfin server plugins can expose APIs and dashboard pages, but they do not automatically get to modify every Jellyfin Web detail page. To make the firearms row appear in Jellyfin Web, load the bundled companion script with a JavaScript injection plugin, File Transformation plugin, or another trusted script-loading method:

```html
<script src="/Imfdb/ClientScript"></script>
```

If you already use a custom JavaScript injector, add that script tag or fetch and run the contents of `/Imfdb/ClientScript`.

## Build

This project targets Jellyfin 10.11.x and .NET 9:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Copy the publish output from `Jellyfin.Plugin.Imfdb/bin/Release/net9.0/publish/` into a Jellyfin plugin folder, then restart Jellyfin.

## Development Status

This is an initial implementation. IMFDB does not provide a stable first-party JSON API for every page, so the plugin uses the public IMFDB Browser pages where possible and falls back to MediaWiki search/parse behavior. Scraping can break if those pages change.

