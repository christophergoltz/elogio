"""
Download GWT cache files for analysis.

Requires cookies.json from discover_api.py (run that first with login).
"""

import json
import os
import requests
from dotenv import load_dotenv

load_dotenv()

# Base URL
BASE_URL = os.getenv("KELIO_URL")  # Required: set in .env file
BASE_URL = BASE_URL.rsplit("/open/", 1)[0]  # Get base domain

# URLs to download (relative paths)
GWT_FILES = [
    "/open/bwt/portail/portail.nocache.js",
    "/open/bwt/portail/85D2B992F6111BC9BF615C4D657B05CC.cache.js",
    "/open/bwt/app_declaration_desktop/app_declaration_desktop.nocache.js",
    "/open/bwt/app_declaration_desktop/1A313ED29AA1E74DD777D2CCF3248188.cache.js",
]

OUTPUT_DIR = "../../docs/gwt-files"
COOKIES_FILE = "cookies.json"


def load_cookies():
    """Load cookies from JSON file exported by discover_api.py."""
    if not os.path.exists(COOKIES_FILE):
        print(f"Error: {COOKIES_FILE} not found.")
        print("Run discover_api.py first to login and export cookies.")
        return None

    with open(COOKIES_FILE, "r", encoding="utf-8") as f:
        cookies = json.load(f)

    print(f"Loaded {len(cookies)} cookies from {COOKIES_FILE}")
    return cookies


def create_session(cookies):
    """Create requests session with cookies."""
    session = requests.Session()
    session.headers.update({
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Referer": f"{BASE_URL}/open/bwt/portail.jsp",
    })

    # Add cookies to session
    for cookie in cookies:
        session.cookies.set(
            cookie["name"],
            cookie["value"],
            domain=cookie.get("domain", ""),
            path=cookie.get("path", "/"),
        )

    return session


def download_file(session, path, output_dir):
    """Download a single file."""
    url = f"{BASE_URL}{path}"
    filename = path.split("/")[-1]

    # Determine subdirectory based on path
    if "portail" in path:
        subdir = os.path.join(output_dir, "portail")
    elif "app_declaration" in path:
        subdir = os.path.join(output_dir, "app_declaration_desktop")
    else:
        subdir = output_dir

    os.makedirs(subdir, exist_ok=True)
    filepath = os.path.join(subdir, filename)

    print(f"Downloading {filename}...")
    print(f"  URL: {url}")

    resp = session.get(url)

    if resp.status_code == 200:
        with open(filepath, "w", encoding="utf-8") as f:
            f.write(resp.text)
        size_kb = len(resp.text) / 1024
        print(f"  Saved to {filepath} ({size_kb:.1f} KB)")
        return True
    else:
        print(f"  Failed: HTTP {resp.status_code}")
        if resp.status_code == 401:
            print("  -> Authentication required. Cookies may have expired.")
            print("  -> Run discover_api.py again to refresh cookies.")
        return False


def main():
    print("=" * 60)
    print("GWT File Downloader (with authentication)")
    print("=" * 60)

    cookies = load_cookies()
    if not cookies:
        return

    session = create_session(cookies)
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    success_count = 0
    for path in GWT_FILES:
        if download_file(session, path, OUTPUT_DIR):
            success_count += 1
        print()

    print("=" * 60)
    print(f"Downloaded {success_count}/{len(GWT_FILES)} files")

    if success_count == len(GWT_FILES):
        print("\nNext step: Run deobfuscate_gwt.py to beautify the JavaScript")


if __name__ == "__main__":
    main()
