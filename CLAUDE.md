# Elogio - Kelio Time & Attendance Client

## Project Overview

Elogio is a WPF desktop client for Kelio Time & Attendance. It reverse-engineers the Kelio BWP (Bodet Web Protocol) to fetch work time data.

## Tech Stack

- **.NET 9** (WPF, Core Library)
- **curl_cffi** (Python) - TLS fingerprint impersonation to bypass server detection
- **GWT-RPC** - Google Web Toolkit Remote Procedure Call protocol

## Project Structure

```
src/
├── Elogio.Core/           # Core library (API client, protocol)
│   ├── Api/               # HTTP clients, Kelio API
│   ├── Protocol/          # BWP codec, GWT-RPC builders
│   ├── Models/            # Data models
│   └── Scripts/           # Python curl_proxy.py
└── Elogio/                # WPF desktop application
tests/
└── Elogio.Tests/          # Integration and unit tests
tools/
└── python/                # Reverse engineering & analysis scripts
docs/
├── BWP Protocol fully reverse-engineering.md
└── gwt-files/             # Downloaded GWT JavaScript for analysis
```

## Known Issues & Solutions

### UTF-8 BOM in Body Files (CRITICAL)

**Problem:** BWP-encoded requests sent via body file were rejected by the server with `ExceptionBWT`.

**Root Cause:** C#'s `Encoding.UTF8` writes a BOM (Byte Order Mark, `0xFEFF`) at the start of files. The Kelio server expects BWP data to start directly with the `0xA4` marker.

**Solution:** Use `UTF8Encoding` without BOM:

```csharp
// WRONG - includes BOM
await File.WriteAllTextAsync(path, body, Encoding.UTF8);

// CORRECT - no BOM
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
await File.WriteAllTextAsync(path, body, utf8NoBom);
```

**File:** `src/Elogio.Core/Api/CurlImpersonateClient.cs` - `PostWithBodyFileAsync()`

### TLS Fingerprinting (JA3/JA4)

**Problem:** Standard .NET HttpClient gets 401 Unauthorized on BWP requests.

**Root Cause:** The Kelio server detects TLS fingerprints and rejects non-browser clients.

**Solution:** Use `curl_cffi` Python library via `curl_proxy.py` to impersonate Chrome's TLS fingerprint.

**Files:**
- `src/Elogio.Core/Api/CurlImpersonateClient.cs`
- `src/Elogio.Core/Scripts/curl_proxy.py`

### Dynamic Employee ID

**Problem:** Hardcoded employee ID (227) caused `ExceptionBWT` errors.

**Root Cause:** The employee ID is session-specific and must be extracted from `GlobalBWTService.connect()` response.

**Solution:** Call `GlobalBWTService.connect()` during login and extract employee ID from the response. The ID appears near the user's name in the GWT-RPC response.

**File:** `src/Elogio.Core/Api/KelioClient.cs` - `GlobalBwtServiceConnectAsync()`, `ExtractEmployeeIdFromConnectResponse()`

### TLS Fingerprint Consistency

**Problem:** Session established with .NET HttpClient, but BWP requests sent via curl_cffi caused `ExceptionBWT`.

**Root Cause:** The server tracks TLS fingerprints per session. Mixing .NET and curl_cffi fingerprints within one session triggers server-side errors.

**Solution:** ALL HTTP requests must go through `curl_cffi` - login, portal, connect, and all API calls.

## BWP Protocol

BWP (Bodet Web Protocol) is a simple XOR-based encoding:

```
Format: [0xA4][KEY_COUNT][KEYS...][ENCODED_BODY]

Encoding:
- Marker: 0xA4 (¤)
- Key count: chr(48 + N)
- Keys: chr(48 + key[i] + (i % 11))
- Body: chr(charCode + key[i % N] - (i % 17))

Decoding:
- Key count: charCode(1) - 48
- Keys[i]: charCode(2+i) - 48 - (i % 11)
- Body[i]: charCode - key[i % N] + (i % 17)
```

## API Endpoints

**Main endpoint:** `POST /open/bwpDispatchServlet?{timestamp}`

**Key services:**
- `PortailBWTService.connect` - Initial portal connection
- `GlobalBWTService.connect` - Returns employee ID and user info
- `GlobalBWTService.getTraductions` - Translations (must be called before getSemaine)
- `DeclarationPresenceCompteurBWTService.getSemaine` - Week presence/time data

## Running Tests

```bash
# All integration tests
dotnet test --filter "Category=Integration"

# Specific test
dotnet test --filter "FullyQualifiedName~GetWeekPresence"
```

## Debug Logging

HTTP traffic is logged to: `%LOCALAPPDATA%\Elogio\http_log.txt`

## Git Workflow

- Christopher handles all commits (Claude has read-only git access)
- Suggest conventional commit messages: `type(scope): description`
- Comments in code must be in English

---

## Development Process: 5-Phase System

**ALWAYS follow this structured approach for any implementation task.**

### Phase 1: Requirements Analysis

**Goal:** Fully understand what needs to be done.

- Analyze the requirement thoroughly
- Ask clarifying questions if ANYTHING is unclear
- **NEVER start with incomplete understanding**
- No half-measures - either fully understand or ask first
- Document assumptions and constraints

### Phase 2: Current State Analysis

**Goal:** Understand the existing codebase.

- Analyze relevant existing code
- Identify affected components and dependencies
- Understand current architecture patterns in use
- Note any technical debt or constraints

### Phase 3: Solution Design

**Goal:** Define HOW to implement the solution.

- If multiple approaches exist:
  - Present options with pros/cons
  - Include personal recommendation with reasoning
  - Reference community best practices where relevant
  - **Decide together** before proceeding
- Keep discussion architectural (no code examples unless requested)
- Consider impact on existing code
- Identify potential risks

### Phase 4: Implementation Plan

**Goal:** Create a concrete, actionable plan.

- Based on the agreed solution from Phase 3
- Break down into specific steps
- Identify files to create/modify
- Define order of implementation
- Plan for testing approach

### Phase 5: Implementation

**Goal:** Execute the plan with quality.

- Implement according to the plan
- Write clean, tested code
- After completion: suggest a **Conventional Commit** message
- Format: `type(scope): description`
- Types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`

---

## Code Quality Standards

### Clean Code Principles

- **Readable:** Code should be self-documenting
- **Simple:** Prefer simple solutions over clever ones
- **DRY:** Don't Repeat Yourself - but don't over-abstract either
- **Small Functions:** Each function does one thing well
- **Meaningful Names:** Variables, methods, classes have clear intent
- **No Magic Numbers:** Use constants with descriptive names

### SOLID Principles

- **S**ingle Responsibility: One class, one reason to change
- **O**pen/Closed: Open for extension, closed for modification
- **L**iskov Substitution: Subtypes must be substitutable for base types
- **I**nterface Segregation: Many specific interfaces over one general
- **D**ependency Inversion: Depend on abstractions, not concretions

### Quality Expectations

- **No quick hacks** - take time to do it right
- **Testable code** - design for testability
- **Consistent patterns** - follow existing project conventions
- **Error handling** - handle edge cases properly
- **Performance awareness** - consider efficiency without premature optimization

## CRITICAL: No Company Names in Code

**NEVER hardcode company names (like customer subdomains) in source code.**

All customer-specific values must be:
- Loaded from environment variables, user secrets, or config files
- Stored outside of git (e.g., `.env`, User Secrets, `appsettings.local.json`)

Examples:
- Server URLs: `https://{company}.kelio.io` → load from config
- Test credentials: Use User Secrets (`dotnet user-secrets`)
- Python scripts: Use `.env` file (already in `.gitignore`)

This prevents accidental exposure of customer information in the repository.
