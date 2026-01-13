"""
Analyze BWP (Bodet Web Protocol) format from captured requests.
"""

import json
import re

def load_discovery():
    with open("api_discovery.json", "r", encoding="utf-8") as f:
        return json.load(f)

def analyze_gwt_request(post_data: str):
    """Analyze GWT RPC serialized request."""
    if not post_data:
        return None

    # Standard GWT RPC format starts with version number
    if post_data.startswith("10,") or post_data.startswith("11,"):
        # GWT RPC format: version, typeTable, stringTable, data
        parts = post_data.split(",")
        print(f"  GWT RPC Version: {parts[0]}")

        # Extract Java class names
        classes = re.findall(r'"(com\.bodet\.[^"]+)"', post_data)
        if classes:
            print(f"  Java Classes: {classes[:5]}...")

        # Extract method names
        methods = re.findall(r'"([a-z][a-zA-Z]+)"', post_data)
        if methods:
            print(f"  Possible methods: {methods[:10]}")

        return {"type": "gwt_rpc", "classes": classes, "methods": methods}

    # BWP encoded format (starts with special char)
    if post_data.startswith("¤") or ord(post_data[0]) > 127:
        print(f"  BWP Encoded format")
        print(f"  First 10 bytes: {[ord(c) for c in post_data[:10]]}")
        print(f"  Raw start: {repr(post_data[:50])}")

        # Try to find patterns
        # Check if it's XOR encoded or similar
        return {"type": "bwp_encoded", "length": len(post_data)}

    return {"type": "unknown", "preview": post_data[:100]}


def analyze_gwt_response(body: str):
    """Analyze GWT RPC response."""
    if not body:
        return None

    # Standard GWT RPC response
    if body.startswith("//OK") or body.startswith("//EX"):
        print(f"  GWT RPC Response: {'OK' if body.startswith('//OK') else 'Exception'}")
        return {"type": "gwt_rpc_response"}

    # Numeric response (subscription IDs etc)
    if body.isdigit():
        print(f"  Numeric response: {body}")
        return {"type": "numeric", "value": int(body)}

    # BWP encoded
    if body.startswith("¤") or (len(body) > 0 and ord(body[0]) > 127):
        print(f"  BWP Encoded response")
        return {"type": "bwp_encoded"}

    # Large responses with Java class serialization
    if "com.bodet" in body:
        classes = re.findall(r'"(com\.bodet\.[^"]+)"', body)
        print(f"  GWT Serialized - Classes: {classes[:5]}...")
        return {"type": "gwt_serialized", "classes": classes}

    return {"type": "unknown"}


def main():
    data = load_discovery()

    print("=" * 60)
    print("BWP Protocol Analysis")
    print("=" * 60)

    # Find all bwpDispatchServlet requests
    dispatch_requests = [
        e for e in data
        if e.get("type") == "request" and "bwpDispatchServlet" in e.get("url", "")
    ]

    print(f"\nFound {len(dispatch_requests)} bwpDispatchServlet requests\n")

    # Analyze each unique request pattern
    seen_patterns = set()

    for req in dispatch_requests[:10]:  # First 10
        post_data = req.get("post_data", "")
        if not post_data:
            continue

        # Create pattern signature
        if post_data.startswith("10,") or post_data.startswith("11,"):
            # Extract service name for grouping
            match = re.search(r'"(com\.bodet\.[^"]+Service)"', post_data)
            pattern = match.group(1) if match else "unknown_service"
        else:
            pattern = f"bwp_encoded_{len(post_data)}"

        if pattern in seen_patterns:
            continue
        seen_patterns.add(pattern)

        print(f"\n--- Request Pattern: {pattern} ---")
        print(f"URL: {req['url']}")
        analyze_gwt_request(post_data)

    # Find responses with actual data
    print("\n" + "=" * 60)
    print("Response Analysis")
    print("=" * 60)

    responses_with_body = [
        e for e in data
        if e.get("type") == "response"
        and e.get("body")
        and "bwpDispatchServlet" in e.get("url", "")
    ]

    print(f"\nFound {len(responses_with_body)} responses with bodies\n")

    for resp in responses_with_body[:5]:
        print(f"\n--- Response from {resp['url'][:60]}... ---")
        print(f"Status: {resp['status']}")
        analyze_gwt_response(resp.get("body", ""))

    # Analyze BWP encoding
    print("\n" + "=" * 60)
    print("BWP Encoding Analysis")
    print("=" * 60)

    bwp_samples = [
        e.get("post_data") or e.get("body")
        for e in data
        if (e.get("post_data", "").startswith("¤") or
            (e.get("body", "") and e.get("body", "").startswith("¤")))
    ]

    if bwp_samples:
        print(f"\nFound {len(bwp_samples)} BWP encoded samples")

        # Analyze first sample
        sample = bwp_samples[0]
        print(f"\nSample analysis:")
        print(f"  Length: {len(sample)}")
        print(f"  Bytes: {[ord(c) for c in sample[:20]]}")

        # Check for XOR pattern with common keys
        print("\n  Trying XOR decode with common keys...")
        for key in [0x20, 0x30, 0x40, 0x50, 0x55, 0xAA]:
            decoded = ''.join(chr(ord(c) ^ key) for c in sample[1:21])
            if decoded.isprintable():
                print(f"    Key 0x{key:02X}: {repr(decoded)}")


if __name__ == "__main__":
    main()
