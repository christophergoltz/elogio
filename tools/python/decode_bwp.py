"""
Attempt to decode BWP (Bodet Web Protocol) encoding.
"""

import json

def load_discovery():
    with open("api_discovery.json", "r", encoding="utf-8") as f:
        return json.load(f)

def analyze_encoding(data: str):
    """Deep analysis of BWP encoding."""
    if not data or not data.startswith("¤"):
        return

    print(f"Input length: {len(data)}")
    print(f"First 100 chars: {repr(data[:100])}")
    print()

    # Skip the marker
    content = data[1:]

    # Analyze byte distribution
    byte_counts = {}
    for c in content:
        b = ord(c)
        byte_counts[b] = byte_counts.get(b, 0) + 1

    print("Byte frequency (top 20):")
    for b, count in sorted(byte_counts.items(), key=lambda x: -x[1])[:20]:
        char_repr = chr(b) if 32 <= b < 127 else f"0x{b:02X}"
        print(f"  {b:3d} ({char_repr}): {count}")

    print()

    # The format seems to have a header of ASCII chars, then data
    # Let's find where printable text ends
    header_end = 0
    for i, c in enumerate(content):
        if ord(c) < 32 or ord(c) > 126:
            if ord(c) not in [0x1b, 0x1c, 0x1d, 0x1e, 0x1f]:  # Some control chars
                header_end = i
                break

    print(f"Header portion (printable): {repr(content[:header_end])}")
    print()

    # Try various decodings
    print("Attempting various decodings on header...")

    # Base64-like?
    header = content[:header_end] if header_end > 0 else content[:50]

    # Try simple shifts
    for shift in range(1, 10):
        decoded = ''.join(chr((ord(c) - shift) % 256) for c in header[:30])
        if decoded.isascii() and any(c.isalpha() for c in decoded):
            print(f"  Shift -{shift}: {repr(decoded)}")

    # Try XOR with single byte
    for key in [0x20, 0x30, 0x40, 0x50]:
        decoded = ''.join(chr(ord(c) ^ key) for c in header[:30])
        print(f"  XOR 0x{key:02X}: {repr(decoded)}")

    print()

    # Look for patterns that might be GWT-like serialization
    # GWT uses pipe | and comma , as separators
    print("Looking for separator patterns...")
    for sep in [',', '|', ';', ':', '\n', '\t']:
        parts = content.split(sep)
        if 5 < len(parts) < 100:
            print(f"  Split by '{sep}': {len(parts)} parts")
            print(f"    First 5: {parts[:5]}")


def find_known_strings():
    """Look for known strings that might appear in decoded form."""
    data = load_discovery()

    # Find BWP encoded samples
    samples = []
    for e in data:
        post_data = e.get("post_data") or ""
        body = e.get("body") or ""

        if post_data.startswith("¤"):
            samples.append(("request", e.get("url", ""), post_data))
        if body.startswith("¤"):
            samples.append(("response", e.get("url", ""), body))

    print(f"Found {len(samples)} BWP encoded samples\n")

    # Known strings that might appear (from GWT classes we saw)
    known = [
        "BWPRequest", "BWPResponse", "Portail", "Declaration",
        "connect", "subscribe", "getUser", "getData",
        "Absence", "Indicateur", "Alerte"
    ]

    for sample_type, url, content in samples[:3]:
        print(f"\n{'='*60}")
        print(f"{sample_type.upper()}: {url}")
        print(f"{'='*60}")
        analyze_encoding(content)

        # Search for known strings with various encodings
        print("\nSearching for known strings...")
        for s in known:
            if s.lower() in content.lower():
                print(f"  Found '{s}' in plaintext!")

            # Try shifted versions
            for shift in range(1, 5):
                shifted = ''.join(chr((ord(c) + shift) % 256) for c in s)
                if shifted in content:
                    print(f"  Found '{s}' with shift +{shift}")


def main():
    print("BWP Decoding Analysis")
    print("=" * 60)
    find_known_strings()


if __name__ == "__main__":
    main()
