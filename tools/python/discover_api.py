"""
Kelio API Discovery Script

This script logs into Kelio and records all API requests/responses
to help reverse-engineer the backend API.
"""

import json
import os
from datetime import datetime
from dotenv import load_dotenv
from playwright.sync_api import sync_playwright, Request, Response

# Load environment variables
load_dotenv()

KELIO_USERNAME = os.getenv("KELIO_USERNAME")
KELIO_PASSWORD = os.getenv("KELIO_PASSWORD")
KELIO_URL = os.getenv("KELIO_URL")  # Required: set in .env file

# Storage for captured requests
captured_requests: list[dict] = []


def log_request(request: Request):
    """Log outgoing requests."""
    # Skip non-API requests (images, fonts, etc.)
    resource_type = request.resource_type
    if resource_type in ["image", "font", "stylesheet", "media"]:
        return

    entry = {
        "timestamp": datetime.now().isoformat(),
        "type": "request",
        "method": request.method,
        "url": request.url,
        "resource_type": resource_type,
        "headers": dict(request.headers),
        "post_data": None,
    }

    # Try to get POST data
    try:
        post_data = request.post_data
        if post_data:
            entry["post_data"] = post_data
            # Try to parse as JSON
            try:
                entry["post_data_json"] = json.loads(post_data)
            except (json.JSONDecodeError, TypeError):
                pass
    except Exception:
        pass

    captured_requests.append(entry)

    # Console output (handle encoding issues on Windows)
    print(f"\n{'='*60}")
    print(f"[REQUEST] {request.method} {request.url}")
    if entry.get("post_data_json"):
        print(f"  Body (JSON): {json.dumps(entry['post_data_json'], indent=2)[:500]}")
    elif entry.get("post_data"):
        # Escape non-ASCII characters for console output
        body_preview = entry['post_data'][:500].encode('ascii', 'replace').decode('ascii')
        print(f"  Body: {body_preview}")


def log_response(response: Response):
    """Log incoming responses."""
    request = response.request
    resource_type = request.resource_type

    # Skip non-API responses
    if resource_type in ["image", "font", "stylesheet", "media"]:
        return

    entry = {
        "timestamp": datetime.now().isoformat(),
        "type": "response",
        "method": request.method,
        "url": response.url,
        "status": response.status,
        "status_text": response.status_text,
        "headers": dict(response.headers),
        "body": None,
    }

    # Try to get response body for API calls
    content_type = response.headers.get("content-type", "")
    if "json" in content_type or "text" in content_type:
        try:
            body = response.text()
            entry["body"] = body[:5000]  # Limit size
            # Try to parse as JSON
            try:
                entry["body_json"] = json.loads(body)
            except (json.JSONDecodeError, TypeError):
                pass
        except Exception:
            pass

    captured_requests.append(entry)

    # Console output (handle encoding issues on Windows)
    status_emoji = "[OK]" if response.status < 400 else "[ERR]"
    print(f"{status_emoji} {response.status} {response.url[:80]}")
    if entry.get("body_json"):
        body_preview = json.dumps(entry["body_json"], indent=2)[:300]
        body_preview = body_preview.encode('ascii', 'replace').decode('ascii')
        print(f"  Response: {body_preview}")


def save_results():
    """Save captured requests to JSON file."""
    output_file = "api_discovery.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(captured_requests, f, indent=2, ensure_ascii=False)
    print(f"\n{'='*60}")
    print(f"Saved {len(captured_requests)} entries to {output_file}")


def export_cookies(context):
    """Export browser cookies to JSON file for use with requests library."""
    cookies = context.cookies()

    # Convert to requests-compatible format
    cookies_for_requests = []
    for cookie in cookies:
        cookies_for_requests.append({
            "name": cookie["name"],
            "value": cookie["value"],
            "domain": cookie["domain"],
            "path": cookie["path"],
            "secure": cookie.get("secure", False),
            "httpOnly": cookie.get("httpOnly", False),
            "expires": cookie.get("expires", -1),
        })

    output_file = "cookies.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(cookies_for_requests, f, indent=2)
    print(f"Exported {len(cookies_for_requests)} cookies to {output_file}")


def main():
    import sys

    # Check for quick mode (just login and export cookies)
    quick_mode = "--quick" in sys.argv or "-q" in sys.argv

    if not KELIO_USERNAME or not KELIO_PASSWORD:
        print("Error: Please set KELIO_USERNAME and KELIO_PASSWORD in .env file")
        print("Copy .env.example to .env and fill in your credentials")
        return

    if quick_mode:
        print("Quick mode: Login, export cookies, and exit")
    print(f"Starting Kelio API Discovery")
    print(f"URL: {KELIO_URL}")
    print(f"User: {KELIO_USERNAME}")
    print("="*60)

    with sync_playwright() as p:
        # Launch browser (headless in quick mode)
        browser = p.chromium.launch(headless=quick_mode)
        context = browser.new_context()
        page = context.new_page()

        # Attach request/response listeners (skip in quick mode for speed)
        if not quick_mode:
            page.on("request", log_request)
            page.on("response", log_response)

        print(f"\nNavigating to login page...")
        page.goto(KELIO_URL)

        # Wait for page to load
        page.wait_for_load_state("networkidle")

        print("\nLooking for login form...")

        # Try to find and fill login form
        # Common selectors for username/password fields
        username_selectors = [
            'input[name="username"]',
            'input[name="login"]',
            'input[name="user"]',
            'input[name="email"]',
            'input[type="text"]',
            'input[id*="user"]',
            'input[id*="login"]',
            '#username',
            '#login',
        ]

        password_selectors = [
            'input[name="password"]',
            'input[name="pass"]',
            'input[type="password"]',
            '#password',
        ]

        submit_selectors = [
            '#okButton',  # Kelio specific
            'input#okButton',
            'button[type="submit"]',
            'input[type="submit"]',
            'button:has-text("Login")',
            'button:has-text("Anmelden")',
            'button:has-text("Sign in")',
            'button:has-text("Connexion")',
            'input[value="Bestätigen"]',
        ]

        # Find username field
        username_field = None
        for selector in username_selectors:
            try:
                if page.locator(selector).count() > 0:
                    username_field = page.locator(selector).first
                    print(f"  Found username field: {selector}")
                    break
            except Exception:
                continue

        # Find password field
        password_field = None
        for selector in password_selectors:
            try:
                if page.locator(selector).count() > 0:
                    password_field = page.locator(selector).first
                    print(f"  Found password field: {selector}")
                    break
            except Exception:
                continue

        if username_field and password_field:
            print("\nFilling login form...")
            username_field.fill(KELIO_USERNAME)
            password_field.fill(KELIO_PASSWORD)

            # Find and click submit button
            for selector in submit_selectors:
                try:
                    if page.locator(selector).count() > 0:
                        print(f"  Clicking submit: {selector}")
                        page.locator(selector).first.click()
                        break
                except Exception:
                    continue

            # Wait for navigation after login
            print("\nWaiting for login to complete...")
            try:
                # Wait for URL to change (indicates successful login redirect)
                page.wait_for_url("**/bwt/**", timeout=15000)
            except Exception:
                # Fallback: just wait for load
                page.wait_for_load_state("load", timeout=15000)
            page.wait_for_timeout(2000)  # Extra wait for async requests

            # Export cookies after successful login
            export_cookies(context)
        else:
            print("\nCould not find login form automatically.")
            print("Please log in manually in the browser window.")

        # In quick mode, just export cookies and exit
        if quick_mode:
            print("\n" + "="*60)
            print("Quick mode complete. Cookies exported to cookies.json")
            browser.close()
            print("\nDone! Run 'python download_gwt.py' to download GWT files.")
            return

        print("\n" + "="*60)
        print("Browser is open. Navigate to the pages you want to analyze.")
        print("All API requests are being recorded.")
        print("Press Enter in this console when done to save results and exit.")
        print("="*60)

        # Wait for user to finish exploring
        input()

        save_results()
        browser.close()

    print("\nDone! Check api_discovery.json for all captured API calls.")


if __name__ == "__main__":
    main()
