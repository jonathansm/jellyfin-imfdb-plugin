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

## Install From A Jellyfin Repository

Once this project is published to GitHub with GitHub Pages enabled, users can install it from a Jellyfin plugin repository URL:

```text
https://YOUR_GITHUB_USERNAME.github.io/jellyfin-imfdb/manifest.json
```

In Jellyfin:

1. Go to Dashboard -> Plugins -> Repositories.
2. Add the repository URL above.
3. Go to Catalog.
4. Install IMFDB.
5. Restart Jellyfin.

This plugin also requires the File Transformation plugin repository:

```text
https://www.iamparadox.dev/jellyfin/plugins/manifest.json
```

## Build

This project targets Jellyfin 10.11.x and .NET 9:

```bash
dotnet publish Jellyfin.Plugin.Imfdb.sln -c Release
```

Copy the publish output from `Jellyfin.Plugin.Imfdb/bin/Release/net9.0/publish/` into a Jellyfin plugin folder, then restart Jellyfin.

## Package A Release Locally

```bash
./scripts/package-plugin.sh 0.1.0.0
```

This creates:

```text
artifacts/jellyfin-plugin-imfdb_0.1.0.0.zip
artifacts/jellyfin-plugin-imfdb_0.1.0.0.zip.md5
```

## Publish A GitHub Release

1. Replace `YOUR_GITHUB_USERNAME` in `Directory.Build.props`, `build.yaml`, and this README.
2. Push the repository to GitHub.
3. Enable GitHub Pages for the `gh-pages` branch in the repository settings.
4. Create and push a version tag:

```bash
git tag v0.1.0.0
git push origin main --tags
```

The release workflow will:

- build the plugin
- upload a release zip and `.md5`
- generate `manifest.json`
- publish the manifest to the `gh-pages` branch

Users then add this repository URL in Jellyfin:

```text
https://YOUR_GITHUB_USERNAME.github.io/jellyfin-imfdb/manifest.json
```

## Development Status

This is an initial implementation. IMFDB does not provide a stable first-party JSON API for every page, so the plugin uses the public IMFDB Browser pages where possible and falls back to MediaWiki search/parse behavior. Scraping can break if those pages change.
