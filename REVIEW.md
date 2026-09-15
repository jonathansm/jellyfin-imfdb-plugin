# Code review and Jellyfin 12 preparation

Reviewed 2026-09-15. Scope: all plugin C#, embedded JavaScript and configuration HTML, project/build metadata, packaging script, release manifest, CI/release workflows, and documentation. The starting working tree was clean; no automated tests existed.

## Findings addressed

| Priority | Finding | Change |
| --- | --- | --- |
| High | A signed-in user could request metadata/images for an item outside their permitted libraries. | Both item endpoints resolve the authenticated user and call Jellyfin's visibility-aware item lookup. Administrator API keys retain server-wide access. |
| High | The assembly targeted Jellyfin 10.11/.NET 9, which cannot serve as the Jellyfin 12 build. | Target Jellyfin.Controller/Model 12.0.0 and net10.0; update build, package and release ABI to 12.0.0.0. |
| High | Legacy X-Emby-Token and api_key authentication fails with Jellyfin 12 defaults. | Use Authorization: MediaBrowser and the supported ApiKey image query parameter, retaining the server base URL. |
| Medium | Every episode in a season wrote the same cache file. | Add an episode ID directory and reject mismatched item IDs, cache versions, title/year changes, and malformed cache envelopes. |
| Medium | Network/API errors became successful empty lookups and could replace useful cached results. | Propagate failures, return HTTP 502 for upstream failures, and avoid cache writes on failure. Successful empty searches remain cacheable. |
| Medium | Search candidates could qualify solely because their snippet contained the production year. | Require a title match before awarding the year bonus; retain letters/numbers from non-Latin titles during normalization. |
| Medium | DOM placement depended on English cast headings and could select a hidden cached page. | Scope lookup to the visible detail page and Jellyfin 12's castCollapsible container, including titles without cast. |
| Medium | Failures could trigger repeated immediate requests; frequent DOM mutations could defer updates indefinitely. | Add a 30-second retry delay and coalesce updates without resetting a pending timer. Guard late results against navigation. |
| Medium | Fire-and-forget cache writes outlived requests; image failures without a cached filename never recommended repair. | Await cache writes, propagate caller cancellation, and recommend refresh when an original image lacks a cached file. |
| Medium | Static lock dictionaries grew forever with each item/image, and image buffering had no practical size limit. | Use fixed lock pools, a 20 MiB response limit and an HTTPS IMFDB image-origin allowlist with redirects disabled. |
| Low | Registration retries were untracked during shutdown. | Run registration through BackgroundService so the host tracks cancellation and completion. |
| Low | Public diagnostics exposed configuration and registration exception details. | Require administrator authentication. ClientScript stays anonymous so the browser can load it. |
| Low | Packaging deleted all previous artifacts; version sorting was lexicographic. | Remove only the requested package, validate four-part versions, and sort manifest versions numerically. |
| Low | No regression checks protected authentication, caches, parsing, or browser integration. | Add server and browser tests and run them in build and release workflows. |

## Compatibility evidence

- [Jellyfin 12 release guidance](https://jellyfin.org/posts/jellyfin-release-12.0/) requires updated third-party plugin builds.
- [Jellyfin v12.0 Controller project](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/MediaBrowser.Controller.csproj) targets .NET 10. The plugin builds against the published 12.0.0 NuGet packages.
- [Jellyfin v12.0 authentication implementation](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs) accepts Authorization: MediaBrowser and ApiKey without legacy authorization.
- [Jellyfin v12.0 Web detail template](https://github.com/jellyfin/jellyfin-web/blob/v12.0/src/apps/legacy/controllers/itemDetails/index.html) contains the castCollapsible anchor.
- File Transformation source reviewed at its current checkout: its reflection registration and callback contract still match this plugin, and its project supports Jellyfin 12. A compatible installed build is required.

## Validation

- Release build: no warnings or errors.
- Server regression tests cover cache isolation/invalidation/corruption, cancellation, path traversal, image refresh, restricted access, upstream failures, candidate selection, wiki parsing, and transformation idempotence/configuration.
- Browser regression tests cover supported auth, image URLs under a base path, visible cast selection, update scheduling, routes, and executable-link rejection. These use a mocked browser environment, not a running Jellyfin UI.
- The 0.3.0.0 package contains only the plugin DLL, XML documentation, and dependency manifest. Historical published entries in manifest.json retain their original ABI and checksums. The release workflow creates the new catalog entry from the release ZIP checksum; no unreleased URL/checksum was added to the catalog.

## Remaining live-server checks

A live Jellyfin server was not available for installation testing. Before publishing 0.3.0.0, install the ZIP alongside a Jellyfin 12-compatible File Transformation build and check:

1. Startup/registration and the configuration page load without errors.
2. With legacy authorization disabled, movie and series cards, cached images, and administrator diagnostics work, including behind a configured base URL.
3. A restricted user cannot fetch lookup or image data for inaccessible items.
4. Navigate rapidly between titles and test a non-English UI and titles without cast. Confirm the row stays attached to the current detail page.
5. Verify a real IMFDB match, warm-cache load, refresh after an upstream outage, and external-link behavior in the clients you support.

Title matching and wiki parsing remain heuristic and cannot guarantee the correct remake/edition. Independent native clients do not execute this web script. Initial uncached responses now wait for cache/image writes; titles with many images or a slow IMFDB connection take longer. The DOM and File Transformation integration remain dependent on upstream implementation details.
