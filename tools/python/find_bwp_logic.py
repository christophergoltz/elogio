"""
Search for BWP serialization logic in GWT JavaScript files.

This tool analyzes the beautified GWT JS to find:
- Content-Type handling (text/bwp)
- Marker byte handling (0xA4 / ¤)
- Serialization/deserialization functions
- String table builders
"""

import os
import re
from collections import defaultdict

# Directories to search
SEARCH_DIRS = ["../../docs/gwt-files/beautified", "../../docs/gwt-files"]


def find_js_files(directories):
    """Find all .js files in given directories."""
    js_files = []
    for directory in directories:
        if not os.path.exists(directory):
            continue
        for root, _, files in os.walk(directory):
            for filename in files:
                if filename.endswith(".js"):
                    js_files.append(os.path.join(root, filename))
    return js_files


def extract_context(content, match, context_chars=200):
    """Extract context around a match."""
    start = max(0, match.start() - context_chars)
    end = min(len(content), match.end() + context_chars)

    prefix = "..." if start > 0 else ""
    suffix = "..." if end < len(content) else ""

    return prefix + content[start:end] + suffix


def search_patterns(content, filename):
    """Search for BWP-related patterns."""
    findings = defaultdict(list)

    # Pattern definitions with descriptions
    patterns = [
        # Content-Type patterns
        (r"text/bwp", "Content-Type: text/bwp"),
        (r"charset=UTF-8", "Charset declaration"),

        # Marker byte patterns (0xA4 = 164 = ¤)
        (r"\\xa4|\\u00a4", "Marker byte escape sequence"),
        (r"String\.fromCharCode\s*\(\s*164\s*\)", "fromCharCode(164)"),
        (r"charCodeAt\s*\([^)]*\)\s*[=!]==?\s*164", "charCodeAt comparison with 164"),
        (r"['\"]¤['\"]", "Literal ¤ character"),
        (r"\b164\b", "Number 164 (potential marker)"),

        # BWP class patterns
        (r"BWPRequest", "BWPRequest class"),
        (r"BWPResponse", "BWPResponse class"),
        (r"bwpDispatchServlet", "bwpDispatchServlet endpoint"),

        # Serialization patterns
        (r"serialize\w*", "Serialize function"),
        (r"deserialize\w*", "Deserialize function"),
        (r"encode\w*", "Encode function"),
        (r"decode\w*", "Decode function"),
        (r"marshal\w*", "Marshal function"),
        (r"unmarshal\w*", "Unmarshal function"),

        # GWT RPC patterns
        (r"StringTable", "String table"),
        (r"TypeSerializer", "Type serializer"),
        (r"AbstractSerializationStream", "GWT serialization stream"),
        (r"SerializationPolicy", "GWT serialization policy"),

        # Bodet-specific patterns
        (r"com\.bodet\.[a-zA-Z.]+", "com.bodet package reference"),

        # Binary/encoding patterns
        (r"btoa|atob", "Base64 functions"),
        (r"ArrayBuffer|Uint8Array", "Binary array types"),
        (r"charCodeAt|fromCharCode", "Character code functions"),
    ]

    for pattern, description in patterns:
        for match in re.finditer(pattern, content, re.IGNORECASE):
            context = extract_context(content, match, 150)
            findings[description].append({
                "match": match.group(),
                "position": match.start(),
                "context": context,
            })

    return findings


def analyze_encoding_function(content):
    """
    Try to find the encoding/decoding function.

    Look for functions that:
    - Check for the 164/0xA4 marker byte
    - Process strings character by character
    - Build output strings
    """
    # Look for function definitions that mention relevant operations
    function_patterns = [
        # Function that uses 164
        r"function\s+\w+\s*\([^)]*\)\s*\{[^}]*164[^}]*\}",
        # Function that uses charCodeAt
        r"function\s+\w+\s*\([^)]*\)\s*\{[^}]*charCodeAt[^}]*\}",
        # Prototype method
        r"\w+\.prototype\.\w+\s*=\s*function[^}]+164[^}]+\}",
    ]

    candidates = []
    for pattern in function_patterns:
        matches = re.finditer(pattern, content, re.DOTALL)
        for match in matches:
            func_text = match.group()
            # Score based on relevance
            score = 0
            if "164" in func_text:
                score += 10
            if "charCodeAt" in func_text:
                score += 5
            if "fromCharCode" in func_text:
                score += 5
            if "serialize" in func_text.lower() or "deserialize" in func_text.lower():
                score += 8
            if len(func_text) < 2000:  # Prefer smaller, focused functions
                score += 3

            if score >= 10:
                candidates.append({
                    "score": score,
                    "position": match.start(),
                    "preview": func_text[:500] + "..." if len(func_text) > 500 else func_text,
                })

    return sorted(candidates, key=lambda x: -x["score"])


def main():
    print("=" * 70)
    print("BWP Serialization Logic Finder")
    print("=" * 70)

    js_files = find_js_files(SEARCH_DIRS)

    if not js_files:
        print("No JavaScript files found.")
        print("Run download_gwt.py and deobfuscate_gwt.py first.")
        return

    print(f"Searching {len(js_files)} JavaScript files...\n")

    all_findings = defaultdict(list)

    for js_file in js_files:
        with open(js_file, "r", encoding="utf-8", errors="replace") as f:
            content = f.read()

        findings = search_patterns(content, js_file)

        if findings:
            print(f"\n{'='*70}")
            print(f"FILE: {js_file}")
            print(f"Size: {len(content) / 1024:.1f} KB")
            print("=" * 70)

            for category, matches in findings.items():
                if matches:
                    print(f"\n  {category}: {len(matches)} occurrence(s)")
                    all_findings[category].extend([
                        {**m, "file": js_file} for m in matches
                    ])

                    # Show first few matches with context
                    for i, match in enumerate(matches[:3]):
                        print(f"    [{i+1}] '{match['match']}' at position {match['position']}")
                        # Show condensed context
                        ctx = match["context"].replace("\n", " ").replace("  ", " ")
                        print(f"        Context: {ctx[:200]}...")

            # Look for encoding function candidates
            candidates = analyze_encoding_function(content)
            if candidates:
                print(f"\n  Potential encoding/decoding functions: {len(candidates)}")
                for i, cand in enumerate(candidates[:2]):
                    print(f"    [{i+1}] Score: {cand['score']} at position {cand['position']}")
                    preview = cand["preview"].replace("\n", " ")[:300]
                    print(f"        {preview}...")

    # Summary
    print("\n" + "=" * 70)
    print("SUMMARY")
    print("=" * 70)

    if all_findings:
        print("\nFindings by category:")
        for category, matches in sorted(all_findings.items(), key=lambda x: -len(x[1])):
            files = set(m["file"] for m in matches)
            print(f"  {category}: {len(matches)} total in {len(files)} file(s)")

        # Key findings for BWP
        print("\nKey findings for BWP analysis:")
        key_categories = [
            "Content-Type: text/bwp",
            "Marker byte escape sequence",
            "fromCharCode(164)",
            "BWPRequest class",
            "Serialize function",
        ]
        for cat in key_categories:
            if cat in all_findings:
                print(f"  [OK] {cat}")
            else:
                print(f"  [ ] {cat}")
    else:
        print("No BWP-related patterns found.")
        print("Make sure the GWT files are downloaded and beautified.")


if __name__ == "__main__":
    main()
