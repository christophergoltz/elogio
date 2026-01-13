"""
Analyze decoded BWP requests/responses to find useful endpoints.
"""

import json
from bwp_codec import BWPCodec


def load_discovery():
    with open("api_discovery.json", "r", encoding="utf-8") as f:
        return json.load(f)


def extract_gwt_info(decoded: str) -> dict:
    """Extract method name, service, and other info from GWT-RPC string."""
    info = {}

    if not decoded:
        return info

    # GWT-RPC format: version, then comma-separated values with quoted strings
    # Find all quoted strings
    import re
    strings = re.findall(r'"([^"]*)"', decoded)

    # Look for service classes
    services = [s for s in strings if "Service" in s]
    if services:
        info["service"] = services[0]

    # Look for method names (usually after session ID, before Service)
    # Common patterns: get*, load*, find*, save*, etc.
    methods = [s for s in strings if s and (
        s.startswith("get") or
        s.startswith("load") or
        s.startswith("find") or
        s.startswith("save") or
        s.startswith("connect") or
        s.startswith("subscribe") or
        s.startswith("init") or
        s.startswith("recherche") or  # French for "search"
        s.startswith("charger") or    # French for "load"
        s.startswith("lire")          # French for "read"
    )]
    if methods:
        info["method"] = methods[0]

    # Look for interesting class names
    classes = [s for s in strings if s.startswith("com.bodet")]
    if classes:
        info["classes"] = classes[:5]

    # Look for date-related strings
    dates = [s for s in strings if any(x in s.lower() for x in ["date", "jour", "heure", "time", "period"])]
    if dates:
        info["date_related"] = dates

    # Store all strings for reference
    info["all_strings"] = strings

    return info


def main():
    print("=" * 70)
    print("Decoded BWP Analysis - Finding Endpoints")
    print("=" * 70)

    data = load_discovery()
    codec = BWPCodec()

    # Collect all decoded requests and responses
    decoded_items = []

    for entry in data:
        url = entry.get("url", "")

        # Only analyze bwpDispatchServlet calls
        if "bwpDispatchServlet" not in url:
            continue

        item = {
            "url": url,
            "type": entry.get("type"),
            "method": entry.get("method"),
            "status": entry.get("status"),
        }

        # Decode request body
        post_data = entry.get("post_data", "")
        if post_data and codec.is_bwp(post_data):
            msg = codec.decode(post_data)
            item["request_decoded"] = msg.decoded
            item["request_info"] = extract_gwt_info(msg.decoded)
        elif post_data:
            item["request_decoded"] = post_data
            item["request_info"] = extract_gwt_info(post_data)

        # Decode response body
        body = entry.get("body", "")
        if body and codec.is_bwp(body):
            msg = codec.decode(body)
            item["response_decoded"] = msg.decoded
            item["response_info"] = extract_gwt_info(msg.decoded)
        elif body:
            item["response_decoded"] = body
            item["response_info"] = extract_gwt_info(body)

        decoded_items.append(item)

    print(f"\nFound {len(decoded_items)} bwpDispatchServlet calls\n")

    # Group by service/method
    by_service = {}
    for item in decoded_items:
        if item["type"] != "request":
            continue

        info = item.get("request_info", {})
        service = info.get("service", "unknown")
        method = info.get("method", "unknown")
        key = f"{service}::{method}"

        if key not in by_service:
            by_service[key] = []
        by_service[key].append(item)

    print("=" * 70)
    print("API Methods Found (grouped by service::method)")
    print("=" * 70)

    for key in sorted(by_service.keys()):
        items = by_service[key]
        print(f"\n{key}")
        print(f"  Calls: {len(items)}")

        # Show first request details
        first = items[0]
        info = first.get("request_info", {})

        if info.get("classes"):
            print(f"  Classes: {info['classes']}")
        if info.get("date_related"):
            print(f"  Date-related: {info['date_related']}")

        # Show decoded preview
        decoded = first.get("request_decoded", "")
        if decoded:
            print(f"  Request preview: {decoded[:150]}...")

    # Now show full details for interesting endpoints
    print("\n" + "=" * 70)
    print("Full Decoded Requests (looking for time/declaration data)")
    print("=" * 70)

    interesting_keywords = [
        "declaration", "pointage", "temps", "heure", "time",
        "absence", "presence", "badge", "punch", "clock"
    ]

    for item in decoded_items:
        if item["type"] != "request":
            continue

        decoded = item.get("request_decoded", "").lower()
        info = item.get("request_info", {})

        # Check if this looks interesting
        is_interesting = any(kw in decoded for kw in interesting_keywords)
        service = info.get("service", "")
        if "Declaration" in service or "Pointage" in service or "Global" in service:
            is_interesting = True

        if is_interesting:
            print(f"\n{'='*70}")
            print(f"Service: {info.get('service', 'N/A')}")
            print(f"Method: {info.get('method', 'N/A')}")
            print(f"{'='*70}")
            print(f"REQUEST:")
            print(item.get("request_decoded", "N/A")[:500])

            # Find matching response
            for resp in decoded_items:
                if resp["type"] == "response" and resp.get("response_decoded"):
                    # Check if URLs match (roughly)
                    print(f"\nRESPONSE PREVIEW:")
                    print(resp.get("response_decoded", "")[:500])
                    break

    # Save all decoded data
    output_file = "decoded_requests.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(decoded_items, f, indent=2, ensure_ascii=False)
    print(f"\n\nSaved all decoded data to {output_file}")


if __name__ == "__main__":
    main()
