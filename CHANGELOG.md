# Changelog

## 0.1.3.4

- Keeps the firearm row attached across Jellyfin Web detail page re-renders.

## 0.1.3.3

- Sends Jellyfin auth headers on plugin lookup requests.
- Retries lookups after transient request failures instead of treating the item as handled.

## 0.1.3.2

- Applies MediaWiki-style file title capitalization before building image URLs.

## 0.1.3.1

- Re-inserts the firearm row after Jellyfin Web re-renders detail sections.
- Strips invisible MediaWiki filename characters before building image URLs.

## 0.1.3.0

- Searches IMFDB directly through the wiki API instead of using IMFDB Browser.
- Replaces placeholder repository manifest checksums with published release checksums.
- Removes unused IMDb id lookup input because IMFDB does not expose reliable IMDb ids.

## 0.1.2.0

- Uses IMFDB wiki search to identify the matching IMFDB title page.
- Builds firearm cards directly from IMFDB wiki sections for more complete title coverage.
- Pulls firearm section prose, first image, and image captions from IMFDB wiki source data.
- Removes legacy appearance/detail-source API fields that were tied to the old Browser table enrichment.
- Stabilizes File Transformation registration ids across Jellyfin restarts.

## 0.1.1.2

- Restyles firearm cards with Jellyfin native card, chapter image, text, and scroll button classes.
- Hides the firearm row scrollbar and adds previous and next controls.
- Removes the IMFDB source text from the Firearms heading.

## 0.1.1.1

- Fixes IMFDB external link handling so desktop web opens a new tab without replacing the current Jellyfin page.
- Improves Jellyfin mobile app compatibility by avoiding optional chaining in the injected client script.

## 0.1.1.0

- Refreshes firearm card styling to better match Jellyfin cast and crew rows.
- Opens IMFDB and details source links in the system browser from Jellyfin mobile apps.
- Filters Wikipedia enrichment results to avoid applying non-firearm images and details.

## 0.1.0.0

- Initial Jellyfin IMFDB plugin.
- Adds an IMFDB lookup API.
- Adds a File Transformation powered Jellyfin Web firearm card row.
- Adds firearm image/detail enrichment where available.
