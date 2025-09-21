# Jellyfin Plugin Subdivx

Subtitle provider for Jellyfin that searches and downloads Spanish subtitles (ESP/SPA) for movies and TV shows. It uses the SubX-Api service (based on Subdivx) and requires an API key to work.

## What It Does
- Integrates a subtitle provider named `Subdivx` into Jellyfin.
- Supports movies and TV episodes.
- Returns Spanish subtitles and lets you customize how results are displayed (title/uploader).
- Optionally uses the original title of the media to improve search matching.

## Requirements
- Jellyfin 10.10 or newer.
- SubX-Api key: https://subx-api.duckdns.org/

## Install via Repository
You can install and keep the plugin updated by adding its repository to Jellyfin.

1) Copy the repository manifest URL:
   - https://raw.githubusercontent.com/lvitti/jellyfin-plugin-subdivx/repo/manifest.json
2) In Jellyfin go to: Dashboard → Plugins → Repositories.
3) Click “Add Repository”.
4) Fill in the fields:
   - Name: Subdivx (or any name you prefer)
   - URL: paste the manifest URL above
5) Save.
6) Go to Plugins → Catalog, search for “Subdivx” and click Install.
7) Restart the Jellyfin server if prompted.

## Plugin Configuration
After installing, open: Dashboard → Plugins → Subdivx → Configuration.

- Api-Key (Token): your SubX-Api key.
- Use Original Title: use the media’s original title when searching.
- Show Title In Result: show the submission title in results.
- Show Uploader In Result: show the uploader name in results.
- SubX API URL (advanced): base URL for SubX-Api (leave empty to use the default).

Save your changes. Then, from any movie/episode page in Jellyfin, use the subtitle search and select “Subdivx” as the provider.

## Notes
- Supported language: Spanish.
