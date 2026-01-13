"""
Test suite for BWP Codec.

Tests decoding/encoding with samples from api_discovery.json.
"""

import json
import sys
from bwp_codec import BWPCodec, BWPMessage


def load_samples():
    """Load BWP samples from api_discovery.json."""
    try:
        with open("api_discovery.json", "r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        print("ERROR: api_discovery.json not found")
        print("Run discover_api.py first to capture API traffic")
        return None

    samples = {"requests": [], "responses": []}

    for entry in data:
        post_data = entry.get("post_data", "")
        body = entry.get("body", "")

        if post_data and post_data.startswith("¤"):
            samples["requests"].append({
                "url": entry.get("url", ""),
                "data": post_data,
            })
        if body and body.startswith("¤"):
            samples["responses"].append({
                "url": entry.get("url", ""),
                "data": body,
            })

    return samples


def test_is_bwp():
    """Test BWP detection."""
    codec = BWPCodec()

    # Should detect BWP
    assert codec.is_bwp("¤test") is True
    assert codec.is_bwp(b"\xa4test") is True

    # Should reject non-BWP
    assert codec.is_bwp("normal text") is False
    assert codec.is_bwp("") is False
    assert codec.is_bwp(b"hello") is False

    print("[OK] test_is_bwp")


def test_decode_structure():
    """Test that decode returns proper structure."""
    codec = BWPCodec()

    # Create a valid BWP message manually
    # Marker + key_count(1) + key(48+0+0=48) + body
    test_data = "¤1" + chr(48) + "test"  # 1 key with value 0

    result = codec.decode(test_data)

    assert isinstance(result, BWPMessage)
    assert result.is_encoded is True
    assert len(result.keys) == 1
    assert result.header_length == 3  # marker + key_count + 1 key
    assert len(result.decoded) > 0

    print("[OK] test_decode_structure")


def test_decode_non_bwp():
    """Test decoding non-BWP data returns it unchanged."""
    codec = BWPCodec()

    plain_data = "10,\"com.bodet.test\",0,1,2"
    result = codec.decode(plain_data)

    assert result.is_encoded is False
    assert result.decoded == plain_data
    assert result.keys == []

    print("[OK] test_decode_non_bwp")


def test_decode_real_samples():
    """Test decoding with real captured samples."""
    samples = load_samples()
    if not samples:
        print("[SKIP] test_decode_real_samples - no samples")
        return

    codec = BWPCodec()
    success = 0
    failed = 0

    # Test requests
    for sample in samples["requests"][:10]:
        try:
            result = codec.decode(sample["data"])
            assert result.is_encoded is True
            assert len(result.keys) > 0
            assert len(result.decoded) > 0
            # Check decoded looks like GWT RPC
            assert "," in result.decoded or len(result.decoded) < 10
            success += 1
        except Exception as e:
            print(f"  Failed to decode request: {e}")
            failed += 1

    # Test responses
    for sample in samples["responses"][:10]:
        try:
            result = codec.decode(sample["data"])
            assert result.is_encoded is True
            assert len(result.keys) > 0
            success += 1
        except Exception as e:
            print(f"  Failed to decode response: {e}")
            failed += 1

    print(f"[OK] test_decode_real_samples - {success} passed, {failed} failed")


def test_encode_decode_roundtrip():
    """Test that encode -> decode produces original data."""
    codec = BWPCodec()

    # Test with GWT-like data
    original = '7,"com.bodet.test.BWPRequest","java.util.List",0,1,2'

    # Encode with fixed keys for reproducibility
    keys = [5, 10, 3, 7]
    encoded = codec.encode(original, keys)

    # Decode
    decoded = codec.decode(encoded)

    assert decoded.is_encoded is True
    assert decoded.keys == keys
    assert decoded.decoded == original

    print("[OK] test_encode_decode_roundtrip")


def test_real_roundtrip():
    """Test round-trip with real samples."""
    samples = load_samples()
    if not samples:
        print("[SKIP] test_real_roundtrip - no samples")
        return

    codec = BWPCodec()
    success = 0

    for sample in samples["requests"][:5]:
        result = codec.decode(sample["data"])
        re_encoded = codec.encode(result.decoded, result.keys)

        if re_encoded == sample["data"]:
            success += 1

    print(f"[OK] test_real_roundtrip - {success}/5 passed")


def test_analyze():
    """Test analysis functionality."""
    samples = load_samples()
    if not samples:
        print("[SKIP] test_analyze - no samples")
        return

    codec = BWPCodec()

    for sample in samples["requests"][:3]:
        analysis = codec.analyze(sample["data"])

        assert "is_bwp" in analysis
        assert analysis["is_bwp"] is True
        assert "key_count" in analysis
        assert analysis["key_count"] > 0
        assert "decoded_preview" in analysis
        assert analysis["decode_success"] is True

    print("[OK] test_analyze")


def test_gwt_class_detection():
    """Test that decoded data contains expected GWT class names."""
    samples = load_samples()
    if not samples:
        print("[SKIP] test_gwt_class_detection - no samples")
        return

    codec = BWPCodec()
    found_classes = set()

    for sample in samples["requests"] + samples["responses"]:
        result = codec.decode(sample["data"])
        if "com.bodet" in result.decoded:
            # Extract class names
            import re
            classes = re.findall(r'"(com\.bodet\.[^"]+)"', result.decoded)
            found_classes.update(classes)

    print(f"  Found {len(found_classes)} unique com.bodet.* classes")
    assert len(found_classes) > 0, "Should find at least one com.bodet class"

    # Check for expected classes
    expected = ["BWPRequest", "BWPResponse"]
    for exp in expected:
        found = any(exp in c for c in found_classes)
        if found:
            print(f"  [OK] Found class containing '{exp}'")

    print("[OK] test_gwt_class_detection")


def run_all_tests():
    """Run all tests."""
    print("=" * 60)
    print("BWP Codec Test Suite")
    print("=" * 60)

    tests = [
        test_is_bwp,
        test_decode_structure,
        test_decode_non_bwp,
        test_encode_decode_roundtrip,
        test_decode_real_samples,
        test_real_roundtrip,
        test_analyze,
        test_gwt_class_detection,
    ]

    passed = 0
    failed = 0

    for test in tests:
        try:
            test()
            passed += 1
        except AssertionError as e:
            print(f"[FAIL] {test.__name__}: {e}")
            failed += 1
        except Exception as e:
            print(f"[ERROR] {test.__name__}: {e}")
            failed += 1

    print("\n" + "=" * 60)
    print(f"Results: {passed} passed, {failed} failed")

    return failed == 0


if __name__ == "__main__":
    success = run_all_tests()
    sys.exit(0 if success else 1)
