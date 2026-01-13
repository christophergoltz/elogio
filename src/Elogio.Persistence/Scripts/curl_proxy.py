#!/usr/bin/env python3
"""
HTTP Proxy script using curl_cffi for TLS fingerprint impersonation.
Called from C# to make requests that bypass TLS fingerprint detection.

Usage:
    python curl_proxy.py <method> <url> [options]

Options:
    --body <data>          Request body (for POST)
    --body-file <path>     Read request body from file (for binary/encoded data)
    --header <key:value>   Add header (can be repeated)
    --cookie <value>       Cookie header value
    --impersonate <target> Browser to impersonate (default: chrome120)

Output (JSON):
    {"status_code": 200, "body": "...", "headers": {...}}
"""

import sys
import io
import json
import argparse
from curl_cffi import requests

# Force UTF-8 output on Windows to handle BWP-encoded binary data
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description="HTTP Proxy with TLS impersonation")
    parser.add_argument("method", help="HTTP method (GET, POST, etc.)")
    parser.add_argument("url", help="Request URL")
    parser.add_argument("--body", default=None, help="Request body")
    parser.add_argument("--body-file", default=None, help="Read request body from file")
    parser.add_argument("--header", action="append", default=[], help="Headers (key:value)")
    parser.add_argument("--cookie", default=None, help="Cookie header value")
    parser.add_argument("--impersonate", default="chrome120", help="Browser to impersonate")

    args = parser.parse_args()

    # Read body from file if specified (takes precedence over --body)
    body = args.body
    if args.body_file:
        try:
            with open(args.body_file, "r", encoding="utf-8") as f:
                body = f.read()
        except Exception as e:
            result = {
                "status_code": -1,
                "body": "",
                "error": f"Failed to read body file: {e}",
                "headers": {}
            }
            print(json.dumps(result, ensure_ascii=False))
            return

    # Build headers dictionary
    headers = {}
    for h in args.header:
        if ":" in h:
            key, value = h.split(":", 1)
            headers[key.strip()] = value.strip()

    try:
        # Use a Session to properly handle cookies
        session = requests.Session()

        # If cookies provided, parse and add to session cookie jar
        # Format: "JSESSIONID=xxx; OTHER=yyy"
        if args.cookie:
            for cookie_part in args.cookie.split(";"):
                cookie_part = cookie_part.strip()
                if "=" in cookie_part:
                    name, value = cookie_part.split("=", 1)
                    # Extract domain from URL
                    from urllib.parse import urlparse
                    parsed = urlparse(args.url)
                    session.cookies.set(name.strip(), value.strip(), domain=parsed.netloc, path="/open")

        response = session.request(
            method=args.method.upper(),
            url=args.url,
            headers=headers if headers else None,
            data=body.encode("utf-8") if body else None,
            impersonate=args.impersonate,
            timeout=30,
            verify=True,
            allow_redirects=False  # Don't follow redirects so we can track session cookies
        )

        # Convert response headers to dict
        response_headers = dict(response.headers)

        # Add response cookies to headers for extraction
        # curl_cffi returns cookies in response.cookies as a Cookies object
        try:
            if response.cookies:
                cookie_strs = []
                # Try to iterate over cookies properly
                for name in response.cookies.keys():
                    value = response.cookies.get(name)
                    cookie_strs.append(f"{name}={value}; Path=/open")
                if cookie_strs:
                    response_headers["Set-Cookie"] = "; ".join(cookie_strs)
        except Exception:
            pass  # If cookie extraction fails, we still have headers from response

        result = {
            "status_code": response.status_code,
            "body": response.text,
            "headers": response_headers
        }

    except Exception as e:
        result = {
            "status_code": -1,
            "body": "",
            "error": str(e),
            "headers": {}
        }

    # Output as JSON to stdout
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
