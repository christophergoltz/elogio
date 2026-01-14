"""
BWP (Bodet Web Protocol) Codec - Encoder/Decoder

Reverse-engineered from Kelio GWT JavaScript.

BWP Format:
    [MARKER][KEY_COUNT][KEYS...][ENCODED_BODY]

Where:
    - MARKER: 0xA4 (¤ character)
    - KEY_COUNT: chr(48 + N) where N is number of keys
    - KEYS: chr(48 + key[i] + (i % 11)) for each key
    - ENCODED_BODY: Each char encoded as chr(charCode + key[i % N] - (i % 17))

Decoding:
    - KEY_COUNT: charCode(1) - 48
    - KEYS[i]: charCode(2+i) - 48 - (i % 11)
    - BODY[i]: charCode - key[(i) % N] + (i % 17)
"""

import json
import random
from dataclasses import dataclass
from typing import Optional


@dataclass
class BWPMessage:
    """Represents a decoded BWP message."""
    raw: str
    is_encoded: bool
    keys: list[int]
    decoded: str
    header_length: int


class BWPCodec:
    """Encoder/Decoder for BWP (Bodet Web Protocol) format."""

    MARKER = 0xA4  # ¤ character
    MARKER_CHAR = chr(MARKER)
    MASK = 0xFFFF  # 16-bit mask (L3e in GWT)

    def __init__(self, debug: bool = False):
        self.debug = debug

    def is_bwp(self, data: str | bytes) -> bool:
        """Check if data is BWP-encoded."""
        if isinstance(data, bytes):
            return len(data) > 0 and data[0] == self.MARKER
        return len(data) > 0 and ord(data[0]) == self.MARKER

    def decode(self, data: str | bytes) -> BWPMessage:
        """
        Decode BWP-encoded data.

        Algorithm (from GWT JS):
        1. Check marker 0xA4 at position 0
        2. Read key count: charCode(1) - 48
        3. Read keys: charCode(2+i) - 48 - (i % 11)
        4. Decode body: char - key[(i) % N] + (i % 17)
        """
        if isinstance(data, bytes):
            data = data.decode("utf-8", errors="replace")

        # If not BWP encoded, return as-is
        if not data or len(data) == 0 or ord(data[0]) != self.MARKER:
            return BWPMessage(
                raw=data,
                is_encoded=False,
                keys=[],
                decoded=data,
                header_length=0
            )

        # Read key count from byte 1
        key_count = ord(data[1]) - 48

        if self.debug:
            print(f"  Key count: {key_count}")

        # Read keys from bytes 2 to 2+key_count
        keys = []
        for i in range(key_count):
            key = ord(data[2 + i]) - 48 - (i % 11)
            keys.append(key)

        if self.debug:
            print(f"  Keys: {keys}")

        # Decode body starting at position 2 + key_count
        header_length = 2 + key_count
        body_start = header_length

        decoded_chars = []
        for i, char in enumerate(data[body_start:]):
            char_code = ord(char)
            key = keys[i % len(keys)]
            decoded_code = (char_code - key + (i % 17)) & self.MASK
            decoded_chars.append(chr(decoded_code))

        decoded = "".join(decoded_chars)

        return BWPMessage(
            raw=data,
            is_encoded=True,
            keys=keys,
            decoded=decoded,
            header_length=header_length
        )

    def encode(self, data: str, keys: list[int] | None = None) -> str:
        """
        Encode data to BWP format.

        Algorithm (from GWT JS):
        1. Generate N random keys (4-37 keys, each 0-14)
        2. Write marker 0xA4 (¤)
        3. Write key count: chr(48 + N)
        4. Write encoded keys: chr(48 + key[i] + (i % 11))
        5. Write encoded body: chr(charCode + key[i % N] - (i % 17))
        """
        if not data:
            return data

        # Generate keys if not provided
        if keys is None:
            key_count = random.randint(4, 37)
            keys = [random.randint(0, 14) for _ in range(key_count)]

        if self.debug:
            print(f"  Encoding with {len(keys)} keys: {keys}")

        result = []

        # Add marker
        result.append(self.MARKER_CHAR)

        # Add key count
        result.append(chr((48 + len(keys)) & self.MASK))

        # Add encoded keys
        for i, key in enumerate(keys):
            encoded_key = (48 + key + (i % 11)) & self.MASK
            result.append(chr(encoded_key))

        # Encode body
        for i, char in enumerate(data):
            char_code = ord(char)
            key = keys[i % len(keys)]
            encoded_code = (char_code + key - (i % 17)) & self.MASK
            result.append(chr(encoded_code))

        return "".join(result)

    def analyze(self, data: str | bytes) -> dict:
        """Analyze BWP data and return detailed information."""
        if isinstance(data, bytes):
            data = data.decode("utf-8", errors="replace")

        result = {
            "is_bwp": self.is_bwp(data),
            "length": len(data),
        }

        if result["is_bwp"]:
            try:
                msg = self.decode(data)
                result["key_count"] = len(msg.keys)
                result["keys"] = msg.keys
                result["header_length"] = msg.header_length
                result["body_length"] = len(data) - msg.header_length
                result["decoded_length"] = len(msg.decoded)
                result["decoded_preview"] = msg.decoded[:500]
                result["decode_success"] = True
            except Exception as e:
                result["decode_error"] = str(e)
                result["decode_success"] = False

        return result


def main():
    """Test the codec with samples from api_discovery.json."""
    print("=" * 60)
    print("BWP Codec - Decoder Test")
    print("=" * 60)

    # Try to load samples
    try:
        with open("api_discovery.json", "r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        print("api_discovery.json not found. Run discover_api.py first.")
        return

    # Find BWP samples
    samples = []
    for entry in data:
        post_data = entry.get("post_data", "")
        body = entry.get("body", "")

        if post_data and post_data.startswith("¤"):
            samples.append(("request", entry.get("url", ""), post_data))
        if body and body.startswith("¤"):
            samples.append(("response", entry.get("url", ""), body))

    print(f"Found {len(samples)} BWP-encoded samples\n")

    codec = BWPCodec(debug=True)

    # Test decoding
    for i, (sample_type, url, content) in enumerate(samples[:5]):
        print(f"\n{'='*60}")
        print(f"Sample {i+1}: {sample_type.upper()}")
        print(f"URL: {url[:60]}...")
        print(f"Encoded length: {len(content)} chars")
        print("=" * 60)

        try:
            msg = codec.decode(content)
            print(f"\nDecoded successfully!")
            print(f"  Keys ({len(msg.keys)}): {msg.keys}")
            print(f"  Decoded length: {len(msg.decoded)}")
            print(f"  Decoded preview:")
            print(f"    {msg.decoded[:300]}...")

            # Verify round-trip
            re_encoded = codec.encode(msg.decoded, msg.keys)
            if re_encoded == content:
                print(f"\n  [OK] Round-trip verification passed!")
            else:
                print(f"\n  [WARN] Round-trip mismatch")
                print(f"    Original:   {content[:100]}...")
                print(f"    Re-encoded: {re_encoded[:100]}...")

        except Exception as e:
            print(f"\nDecode failed: {e}")

    print("\n" + "=" * 60)
    print("Codec ready for use!")
    print("=" * 60)


if __name__ == "__main__":
    main()
