#!/usr/bin/env python3
import hashlib
import json
import os
import re
import sys
import argparse
import tempfile
from datetime import datetime, timezone
from urllib.request import urlopen, Request
from urllib.error import HTTPError, URLError

REPO_DEFAULT = "lvitti/jellyfin-plugin-subdivx"
RAW_MANIFEST_URL_TMPL = "https://raw.githubusercontent.com/{repo}/repo/manifest.json"
ASSET_TEMPLATE = "Jellyfin.Plugin.Subdivx-v{version}.zip"
RELEASE_ASSET_URL = "https://github.com/{repo}/releases/download/v{version}/{asset}"


def md5sum_file(path: str, chunk_size: int = 1024 * 1024) -> str:
    """Compute md5 hash of a file in streaming mode."""
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(chunk_size), b""):
            h.update(chunk)
    return h.hexdigest()


def read_remote_json(url: str, ua: str):
    req = Request(url, headers={"User-Agent": ua})
    with urlopen(req) as f:
        return json.load(f)


def download_asset_to_file(repo: str, version: str, asset_name: str, dest_path: str, ua: str, chunk_size: int = 1024 * 1024):
    """Stream the GitHub release asset to dest_path."""
    url = RELEASE_ASSET_URL.format(repo=repo, version=version, asset=asset_name)
    req = Request(url, headers={"User-Agent": ua})
    with urlopen(req) as r, open(dest_path, "wb") as out:
        while True:
            chunk = r.read(chunk_size)
            if not chunk:
                break
            out.write(chunk)


def parse_version(arg: str) -> str:
    """
    Accepts:
      - Jellyfin.Plugin.Subdivx-v1.0.0.zip
      - v1.0.0
      - 1.0.0
    Returns: '1.0.0'
    """
    fn = os.path.basename(arg.strip())

    m = re.search(r"-v(?P<v>[0-9]+(?:\.[0-9]+)*)\.zip$", fn)
    if m:
        return m.group("v")

    v = fn
    if v.lower().startswith("v"):
        v = v[1:]

    if re.fullmatch(r"[0-9]+(?:\.[0-9]+)*", v):
        return v

    raise ValueError(f"Could not extract version from: {arg!r}")


def build_source_url(repo: str, version: str, asset_name: str) -> str:
    return RELEASE_ASSET_URL.format(repo=repo, version=version, asset=asset_name)


def generate_entry(version: str, checksum: str, source_url: str, target_abi: str) -> dict:
    return {
        "checksum": checksum,
        "changelog": "Auto Released by Actions",
        "targetAbi": target_abi,
        "sourceUrl": source_url,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "version": version,
    }


def main():
    parser = argparse.ArgumentParser(description="Update Jellyfin plugin manifest with a new release entry.")
    parser.add_argument("arg", help="Either the local asset file (Jellyfin.Plugin.Subdivx-vX.Y.Z.zip) or a version (vX.Y.Z | X.Y.Z)")
    parser.add_argument("--repo", default=REPO_DEFAULT, help="GitHub repo in the form owner/name (default: %(default)s)")
    parser.add_argument("--target-abi", default="10.10.x.x", help="Target ABI string to write into manifest (default: %(default)s)")
    args = parser.parse_args()

    repo = args.repo
    ua = f"manifest-bot/1.1 (+https://github.com/{repo})"
    raw_manifest_url = RAW_MANIFEST_URL_TMPL.format(repo=repo)

    version = parse_version(args.arg)
    asset_name = ASSET_TEMPLATE.format(version=version)
    source_url = build_source_url(repo, version, asset_name)

    with tempfile.TemporaryDirectory() as tmpdir:
        if os.path.isfile(args.arg):
            local_path = args.arg
        else:
            local_path = os.path.join(tmpdir, asset_name)
            try:
                download_asset_to_file(repo, version, asset_name, local_path, ua=ua)
            except (HTTPError, URLError) as e:
                raise RuntimeError(f"Failed to download release asset '{asset_name}' for v{version}: {e}")

        checksum = md5sum_file(local_path)

        manifest = read_remote_json(raw_manifest_url, ua=ua)
        if not isinstance(manifest, list) or not manifest:
            raise RuntimeError("Remote manifest has an unexpected structure (expected a non-empty list).")

        versions_list = manifest[0].get("versions", [])
        filtered = [v for v in versions_list if v.get("version") != version]

        entry = generate_entry(version, checksum, source_url, args.target_abi)
        manifest[0]["versions"] = [entry] + filtered

        with open("manifest.json", "w") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)

    print(f"OK: manifest.json updated with v{version}")
    print(f"  targetAbi: {args.target_abi}")
    print(f"  sourceUrl: {source_url}")
    print(f"  md5:       {checksum}")


if __name__ == "__main__":
    main()
