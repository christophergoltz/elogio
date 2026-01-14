"""
HAR File Analyzer for Kelio BWP Protocol

Parses HAR files and decodes BWP-encoded requests/responses to reveal
the exact GWT-RPC format for clock-in/clock-out functionality.

Usage:
    python har_analyzer.py <har_file> [--output <output_dir>]
    python har_analyzer.py --all  # Analyze all HAR files in Downloads
"""

import json
import sys
import os
import re
from pathlib import Path
from dataclasses import dataclass
from typing import Optional

# Import the BWP codec from the same directory
from bwp_codec import BWPCodec


@dataclass
class DecodedRequest:
    """Represents a decoded BWP request/response pair."""
    url: str
    timestamp: str
    request_raw: str
    request_decoded: str
    response_raw: str
    response_decoded: str
    service: Optional[str] = None
    method: Optional[str] = None


def extract_gwt_info(decoded: str) -> tuple[Optional[str], Optional[str]]:
    """Extract service and method name from decoded GWT-RPC string."""
    if not decoded:
        return None, None

    # Find all quoted strings
    strings = re.findall(r'"([^"]*)"', decoded)

    # Look for service classes
    service = None
    method = None

    for s in strings:
        if "Service" in s and s.startswith("com.bodet"):
            service = s
        # Method names are typically short and come before the service
        elif s and len(s) < 30 and not s.startswith("com.") and not s.startswith("java."):
            # Common method prefixes
            if any(s.startswith(prefix) for prefix in [
                "get", "set", "load", "save", "find", "connect", "subscribe",
                "init", "recherche", "charger", "lire", "entree", "sortie"
            ]):
                method = s

    return service, method


def parse_har_file(har_path: str, codec: BWPCodec) -> list[DecodedRequest]:
    """Parse a HAR file and decode all BWP requests/responses."""

    with open(har_path, "r", encoding="utf-8") as f:
        har_data = json.load(f)

    entries = har_data.get("log", {}).get("entries", [])
    decoded_requests = []

    # Find all bwpDispatchServlet requests
    bwp_entries = [
        e for e in entries
        if "bwpDispatchServlet" in e.get("request", {}).get("url", "")
        and e.get("request", {}).get("method") == "POST"
    ]

    print(f"Found {len(bwp_entries)} bwpDispatchServlet requests")

    for entry in bwp_entries:
        request = entry.get("request", {})
        response = entry.get("response", {})

        url = request.get("url", "")
        timestamp = entry.get("startedDateTime", "")

        # Get request body
        post_data = request.get("postData", {})
        request_raw = post_data.get("text", "")

        # Get response body
        response_content = response.get("content", {})
        response_raw = response_content.get("text", "")

        # Decode request
        if request_raw and codec.is_bwp(request_raw):
            msg = codec.decode(request_raw)
            request_decoded = msg.decoded
        else:
            request_decoded = request_raw

        # Decode response
        if response_raw and codec.is_bwp(response_raw):
            msg = codec.decode(response_raw)
            response_decoded = msg.decoded
        else:
            response_decoded = response_raw

        # Extract service/method info
        service, method = extract_gwt_info(request_decoded)

        decoded_requests.append(DecodedRequest(
            url=url,
            timestamp=timestamp,
            request_raw=request_raw,
            request_decoded=request_decoded,
            response_raw=response_raw,
            response_decoded=response_decoded,
            service=service,
            method=method
        ))

    return decoded_requests


def print_decoded_request(req: DecodedRequest, index: int):
    """Pretty print a decoded request."""
    print(f"\n{'='*80}")
    print(f"Request #{index + 1}")
    print(f"{'='*80}")
    print(f"Timestamp: {req.timestamp}")
    print(f"URL: {req.url}")
    print(f"Service: {req.service or 'N/A'}")
    print(f"Method: {req.method or 'N/A'}")
    print(f"\n--- REQUEST (decoded) ---")
    print(req.request_decoded[:1000] if req.request_decoded else "N/A")
    if len(req.request_decoded or "") > 1000:
        print("... (truncated)")
    print(f"\n--- RESPONSE (decoded) ---")
    print(req.response_decoded[:1000] if req.response_decoded else "N/A")
    if len(req.response_decoded or "") > 1000:
        print("... (truncated)")


def save_decoded_data(requests: list[DecodedRequest], output_path: str, har_name: str):
    """Save decoded requests to JSON file."""
    data = []
    for req in requests:
        data.append({
            "url": req.url,
            "timestamp": req.timestamp,
            "service": req.service,
            "method": req.method,
            "request_decoded": req.request_decoded,
            "response_decoded": req.response_decoded,
        })

    output_file = os.path.join(output_path, f"{har_name}_decoded.json")
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    print(f"\nSaved decoded data to: {output_file}")
    return output_file


def find_entreesortie_requests(requests: list[DecodedRequest]) -> list[DecodedRequest]:
    """Find requests that might be entreeSortie (clock-in/clock-out)."""
    matching = []

    for req in requests:
        decoded = (req.request_decoded or "").lower()
        method = (req.method or "").lower()

        # Look for entreeSortie or related methods
        if any(kw in decoded or kw in method for kw in [
            "entreesortie", "entree", "sortie", "punch", "clock", "badge"
        ]):
            matching.append(req)

    return matching


def main():
    if len(sys.argv) < 2:
        print("Usage: python har_analyzer.py <har_file> [--output <output_dir>]")
        print("       python har_analyzer.py --all")
        sys.exit(1)

    codec = BWPCodec()

    # Determine HAR files to analyze
    if sys.argv[1] == "--all":
        # Find all HAR files in Downloads
        downloads = Path.home() / "Downloads"
        har_files = list(downloads.glob("*.har"))
        if not har_files:
            print(f"No HAR files found in {downloads}")
            sys.exit(1)
    else:
        har_files = [Path(sys.argv[1])]

    # Output directory
    output_dir = "."
    if "--output" in sys.argv:
        idx = sys.argv.index("--output")
        if idx + 1 < len(sys.argv):
            output_dir = sys.argv[idx + 1]
            os.makedirs(output_dir, exist_ok=True)

    all_entreesortie = []

    for har_file in har_files:
        print(f"\n{'#'*80}")
        print(f"Analyzing: {har_file.name}")
        print(f"{'#'*80}")

        try:
            requests = parse_har_file(str(har_file), codec)

            # Print all requests
            for i, req in enumerate(requests):
                print_decoded_request(req, i)

            # Save to JSON
            har_name = har_file.stem.replace(".", "_")
            save_decoded_data(requests, output_dir, har_name)

            # Find entreeSortie requests
            entreesortie = find_entreesortie_requests(requests)
            if entreesortie:
                print(f"\n{'*'*80}")
                print(f"FOUND {len(entreesortie)} POTENTIAL entreeSortie REQUESTS:")
                print(f"{'*'*80}")
                for req in entreesortie:
                    print(f"\nService: {req.service}")
                    print(f"Method: {req.method}")
                    print(f"Request: {req.request_decoded[:500]}")
                all_entreesortie.extend(entreesortie)

        except Exception as e:
            print(f"Error analyzing {har_file}: {e}")
            import traceback
            traceback.print_exc()

    # Summary
    print(f"\n{'='*80}")
    print("SUMMARY")
    print(f"{'='*80}")
    print(f"Analyzed {len(har_files)} HAR file(s)")
    print(f"Found {len(all_entreesortie)} potential entreeSortie request(s)")

    if all_entreesortie:
        print("\nentreeSortie Request Format:")
        for req in all_entreesortie:
            print(f"\n  Request:\n    {req.request_decoded}")
            print(f"\n  Response:\n    {req.response_decoded[:500]}")


if __name__ == "__main__":
    main()
