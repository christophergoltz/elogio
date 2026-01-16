#!/usr/bin/env python3
"""
HTTP Proxy script using curl_cffi for TLS fingerprint impersonation.
Called from C# to make requests that bypass TLS fingerprint detection.

Modes:
    CLI Mode (default):
        python curl_proxy.py <method> <url> [options]

    Server Mode:
        python curl_proxy.py --server [--port 5123]

CLI Options:
    --body <data>          Request body (for POST)
    --body-file <path>     Read request body from file (for binary/encoded data)
    --header <key:value>   Add header (can be repeated)
    --cookie <value>       Cookie header value
    --impersonate <target> Browser to impersonate (default: chrome120)

Server Endpoints:
    POST /request          Execute HTTP request (JSON body)
    GET /health            Health check
    POST /shutdown         Stop server

Output (JSON):
    {"status_code": 200, "body": "...", "headers": {...}}
"""

import sys
import io
import json
import argparse
import base64
import threading
from urllib.parse import urlparse
from curl_cffi import requests

# Force UTF-8 output on Windows to handle BWP-encoded binary data
if not sys.stdout.isatty():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

# Global session for server mode - persists TLS connection for reuse
_global_session = None
_shutdown_event = threading.Event()


def get_or_create_session(impersonate="chrome120"):
    """Get global session or create new one for TLS reuse."""
    global _global_session
    if _global_session is None:
        _global_session = requests.Session()
    return _global_session


def execute_request(method, url, headers=None, cookies=None, body=None, body_base64=None, impersonate="chrome120", use_global_session=False):
    """Execute HTTP request and return result dict."""
    try:
        # Decode base64 body if provided (for binary BWP data)
        if body_base64:
            body = base64.b64decode(body_base64).decode("utf-8")

        # Use global session in server mode for TLS reuse
        if use_global_session:
            session = get_or_create_session(impersonate)
            # In server mode, let the global session manage cookies automatically.
            # curl_cffi stores Set-Cookie responses and sends them on subsequent requests.
            # Do NOT set cookies from C# - they may be stale and overwrite correct session cookies.
        else:
            session = requests.Session()
            # In CLI mode, set cookies from parameter since each request is isolated
            if cookies:
                for cookie_part in cookies.split(";"):
                    cookie_part = cookie_part.strip()
                    if "=" in cookie_part:
                        name, value = cookie_part.split("=", 1)
                        parsed = urlparse(url)
                        session.cookies.set(name.strip(), value.strip(), domain=parsed.netloc, path="/open")

        response = session.request(
            method=method.upper(),
            url=url,
            headers=headers if headers else None,
            data=body.encode("utf-8") if body else None,
            impersonate=impersonate,
            timeout=30,
            verify=True,
            allow_redirects=False
        )

        # Convert response headers to dict (preserve original case)
        response_headers = {}
        for key, value in response.headers.items():
            response_headers[key] = value

        # Extract Set-Cookie from response headers if present (may be lowercase)
        # Also add cookies from session for consistency
        try:
            cookie_strs = []

            # First, get cookies from response.cookies (newly set cookies)
            if response.cookies:
                for name in response.cookies.keys():
                    value = response.cookies.get(name)
                    if value:
                        cookie_strs.append(f"{name}={value}; Path=/open")

            # Also check session cookies in case they were updated
            if use_global_session and session.cookies:
                for name in session.cookies.keys():
                    value = session.cookies.get(name)
                    if value and not any(name in s for s in cookie_strs):
                        cookie_strs.append(f"{name}={value}; Path=/open")

            if cookie_strs:
                response_headers["Set-Cookie"] = "; ".join(cookie_strs)
        except Exception:
            pass

        return {
            "status_code": response.status_code,
            "body": response.text,
            "headers": response_headers
        }

    except Exception as e:
        return {
            "status_code": -1,
            "body": "",
            "error": str(e),
            "headers": {}
        }


def run_server(port):
    """Run Flask HTTP server for persistent connections."""
    from flask import Flask, request, jsonify

    app = Flask(__name__)

    # Disable Flask's default logging for cleaner output
    import logging
    log = logging.getLogger('werkzeug')
    log.setLevel(logging.ERROR)

    @app.route('/health', methods=['GET'])
    def health():
        return jsonify({"status": "ok"})

    @app.route('/request', methods=['POST'])
    def handle_request():
        data = request.json
        result = execute_request(
            method=data.get('method', 'GET'),
            url=data.get('url'),
            headers=data.get('headers'),
            cookies=data.get('cookies'),
            body=data.get('body'),
            body_base64=data.get('body_base64'),
            impersonate=data.get('impersonate', 'chrome120'),
            use_global_session=True  # Server mode uses global session for TLS reuse
        )
        return jsonify(result)

    @app.route('/shutdown', methods=['POST'])
    def shutdown():
        _shutdown_event.set()
        func = request.environ.get('werkzeug.server.shutdown')
        if func:
            func()
        return jsonify({"status": "shutting_down"})

    print(f"curl_proxy server starting on port {port}...", flush=True)
    print("READY", flush=True)  # Signal to C# that server is ready

    try:
        app.run(host='127.0.0.1', port=port, threaded=True, use_reloader=False)
    except Exception as e:
        print(f"Server error: {e}", file=sys.stderr, flush=True)
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="HTTP Proxy with TLS impersonation")

    # Server mode arguments
    parser.add_argument("--server", action="store_true", help="Run as HTTP server")
    parser.add_argument("--port", type=int, default=5123, help="Server port (default: 5123)")

    # CLI mode arguments (positional, only required if not in server mode)
    parser.add_argument("method", nargs="?", help="HTTP method (GET, POST, etc.)")
    parser.add_argument("url", nargs="?", help="Request URL")
    parser.add_argument("--body", default=None, help="Request body")
    parser.add_argument("--body-file", default=None, help="Read request body from file")
    parser.add_argument("--header", action="append", default=[], help="Headers (key:value)")
    parser.add_argument("--cookie", default=None, help="Cookie header value")
    parser.add_argument("--impersonate", default="chrome120", help="Browser to impersonate")

    args = parser.parse_args()

    # Server mode
    if args.server:
        run_server(args.port)
        return

    # CLI mode - validate required arguments
    if not args.method or not args.url:
        parser.error("method and url are required in CLI mode")
        return

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

    # Execute request using shared function (CLI mode = no global session)
    result = execute_request(
        method=args.method,
        url=args.url,
        headers=headers if headers else None,
        cookies=args.cookie,
        body=body,
        impersonate=args.impersonate,
        use_global_session=False  # CLI mode creates new session per request
    )

    # Output as JSON to stdout
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
