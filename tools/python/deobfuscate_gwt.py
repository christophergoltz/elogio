"""
Deobfuscate/beautify GWT compiled JavaScript files.

Requires: pip install jsbeautifier
"""

import os
import re
import jsbeautifier

INPUT_DIR = "../../docs/gwt-files"
OUTPUT_DIR = "../../docs/gwt-files/beautified"

# jsbeautifier options for best readability
BEAUTIFIER_OPTIONS = jsbeautifier.default_options()
BEAUTIFIER_OPTIONS.indent_size = 2
BEAUTIFIER_OPTIONS.indent_char = " "
BEAUTIFIER_OPTIONS.max_preserve_newlines = 2
BEAUTIFIER_OPTIONS.preserve_newlines = True
BEAUTIFIER_OPTIONS.keep_array_indentation = False
BEAUTIFIER_OPTIONS.break_chained_methods = True
BEAUTIFIER_OPTIONS.space_before_conditional = True
BEAUTIFIER_OPTIONS.unescape_strings = True


def find_js_files(directory):
    """Find all .js files recursively."""
    js_files = []
    for root, _, files in os.walk(directory):
        # Skip already beautified files
        if "beautified" in root:
            continue
        for filename in files:
            if filename.endswith(".js"):
                js_files.append(os.path.join(root, filename))
    return js_files


def beautify_file(input_path, output_dir):
    """Beautify a single JavaScript file."""
    # Determine output path preserving subdirectory structure
    rel_path = os.path.relpath(input_path, INPUT_DIR)
    output_path = os.path.join(output_dir, rel_path)

    # Create output directory if needed
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    print(f"Processing: {rel_path}")

    with open(input_path, "r", encoding="utf-8", errors="replace") as f:
        content = f.read()

    original_size = len(content)
    print(f"  Original size: {original_size / 1024:.1f} KB")

    # Beautify
    try:
        beautified = jsbeautifier.beautify(content, BEAUTIFIER_OPTIONS)
        beautified_size = len(beautified)
        print(f"  Beautified size: {beautified_size / 1024:.1f} KB")

        with open(output_path, "w", encoding="utf-8") as f:
            f.write(beautified)

        print(f"  Saved to: {output_path}")
        return True
    except Exception as e:
        print(f"  Error: {e}")
        return False


def extract_string_table(content):
    """
    Try to extract GWT string table.

    GWT compiled code often has a string table like:
    var $intern_1 = 'some string', $intern_2 = 'another', ...
    """
    # Pattern for GWT string table entries
    patterns = [
        r"\$intern_\d+\s*=\s*'([^']*)'",
        r"\$intern_\d+\s*=\s*\"([^\"]*)\"",
        r"var\s+(\w+)\s*=\s*'([^']*)'",
    ]

    strings = []
    for pattern in patterns:
        matches = re.findall(pattern, content)
        if matches:
            # Handle both single capture and tuple captures
            for match in matches:
                if isinstance(match, tuple):
                    strings.append(match[-1])
                else:
                    strings.append(match)

    return list(set(strings))  # Deduplicate


def search_bwp_patterns(content):
    """Search for BWP-related patterns in the code."""
    patterns = {
        "text/bwp": r"text/bwp",
        "marker_char": r"\\xa4|\\u00a4|164|'¤'",
        "BWPRequest": r"BWPRequest",
        "BWPResponse": r"BWPResponse",
        "serialize": r"serialize",
        "deserialize": r"deserialize",
        "com.bodet": r"com\.bodet",
        "bwpDispatchServlet": r"bwpDispatchServlet",
    }

    print("\n  BWP-related patterns found:")
    found_any = False

    for name, pattern in patterns.items():
        matches = re.findall(pattern, content, re.IGNORECASE)
        if matches:
            print(f"    {name}: {len(matches)} occurrences")
            found_any = True

    if not found_any:
        print("    (none found)")

    return found_any


def main():
    print("=" * 60)
    print("GWT JavaScript Deobfuscator/Beautifier")
    print("=" * 60)

    if not os.path.exists(INPUT_DIR):
        print(f"Error: {INPUT_DIR} not found.")
        print("Run download_gwt.py first to download the GWT files.")
        return

    js_files = find_js_files(INPUT_DIR)

    if not js_files:
        print(f"No .js files found in {INPUT_DIR}")
        return

    print(f"Found {len(js_files)} JavaScript files\n")
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    success_count = 0
    for js_file in js_files:
        if beautify_file(js_file, OUTPUT_DIR):
            # Search for BWP patterns in the beautified content
            with open(js_file, "r", encoding="utf-8", errors="replace") as f:
                search_bwp_patterns(f.read())
            success_count += 1
        print()

    print("=" * 60)
    print(f"Beautified {success_count}/{len(js_files)} files")
    print(f"Output directory: {OUTPUT_DIR}")

    if success_count > 0:
        print("\nNext step: Search for BWP serialization logic:")
        print("  - Look for 'text/bwp' content type handling")
        print("  - Look for character code 164 (0xA4, ¤)")
        print("  - Look for serialize/deserialize functions")


if __name__ == "__main__":
    main()
