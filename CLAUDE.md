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

**Problem:** Using a static employee ID caused `ExceptionBWT` errors because the ID is session-specific.

**Root Cause:** The employee ID is session-specific and must be extracted from `GlobalBWTService.connect()` response.

**Solution:** Call `GlobalBWTService.connect()` during login and extract employee ID from the response using pattern matching on the GWT-RPC data structure. If employee ID extraction fails, the API will return an error rather than using a fallback value.

**File:** `src/Elogio.Persistence/Api/KelioClient.cs` - `GlobalBwtServiceConnectAsync()`, `ExtractEmployeeIdFromConnectResponse()`

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
- `BadgerSignalerPortailBWTService.badgerSignaler` - Clock-in/clock-out punch (server determines direction)

## Running Tests

```bash
# All integration tests
dotnet test --filter "Category=Integration"

# Specific test
dotnet test --filter "FullyQualifiedName~GetWeekPresence"
```

## Debug Logging

HTTP traffic is logged to: `%LOCALAPPDATA%\Elogio\http_log.txt`

### Accessing Application Logs (for Claude)

**Log location:** `C:\Users\Elonius\AppData\Local\Elogio\logs\elogio-YYYYMMDD.log`

**To read the latest log (use Glob first, then Read):**
```
1. Glob pattern: **/Elogio/logs/*.log with path: C:\Users\Elonius\AppData\Local
2. Read the newest file (sorted by date in filename)
```

**Example:**
```
Glob: C:\Users\Elonius\AppData\Local\Elogio\logs\*.log
Read: C:\Users\Elonius\AppData\Local\Elogio\logs\elogio-20260117.log (offset: 1, limit: 200)
```

**DO NOT use PowerShell or cmd for log access** - environment variables don't expand correctly in the shell.

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

### WPF / MVVM Pattern (CRITICAL)

**ALWAYS follow the MVVM pattern for WPF development.**

- **Views (XAML):** UI only, no business logic
- **ViewModels:** All logic, state, and commands - use `CommunityToolkit.Mvvm`
- **Code-behind (.xaml.cs):** Keep minimal - only use when absolutely necessary (e.g., setting DataContext via DI)

**Rules:**
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for commands
- Use `Command="{Binding ...}"` instead of `Click="..."` event handlers
- All UI updates via data binding, never directly manipulate UI elements from ViewModel
- ViewModels receive dependencies via constructor injection

**Example structure:**
```
ViewModels/
├── MainViewModel.cs
├── LoginViewModel.cs
└── SettingsViewModel.cs
Views/Pages/
├── LoginPage.xaml          # UI only
├── LoginPage.xaml.cs       # Minimal: just DataContext = viewModel
└── SettingsPage.xaml
```

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

## CRITICAL: Windows Reserved Filenames

**NEVER create files with Windows reserved names!**

These names are reserved device names and cannot be easily deleted:
- `nul`, `con`, `prn`, `aux`
- `com1`-`com9`, `lpt1`-`lpt9`

**Common mistakes:**
- Redirecting output to `nul` in a way that creates a file instead of discarding
- Using these names as variable placeholders that end up as filenames

If a `nul` file is accidentally created, delete it with:
```powershell
cmd /c "del \\?\C:\path\to\nul"
```

---

## CRITICAL: No Sensitive Data in Git

**NEVER commit sensitive data to the repository.**

### Pre-Commit Review (MANDATORY)

After implementing a feature, Claude MUST:
1. Run `git diff` and `git status` to review all changes
2. Identify files with potentially sensitive data
3. **Point out any issues to Christopher** before committing
4. Discuss how to handle each case (anonymize, gitignore, or delete)

### Sensitive Data Includes

- **Employee IDs** (e.g., `958`, `852`) - real IDs from HAR captures
- **Employee names** (e.g., `"Christopher"`, `"Goltz"`)
- **Session IDs** (real GUIDs from captures)
- **Company/Customer names** (subdomains like `pharmagest.kelio.io`)
- **HAR files** and decoded API responses
- **Credentials** of any kind

### Handling Options

| Option | When to Use |
|--------|-------------|
| **Anonymize** | Test fixtures: replace with fake data (`00000000-...`, `12345`) |
| **Gitignore** | Generated files, decoded data, HAR captures |
| **Delete** | Temp files, one-time analysis outputs |

### Already in .gitignore

```
tools/python/decoded/     # Decoded HAR data
tools/python/cookies.json # Session cookies
tools/python/.env         # Python secrets
*.har                     # HAR capture files
```

### For Production Code

- Employee IDs: Extract dynamically from `GlobalBWTService.connect()`
- Server URLs: Load from config, never hardcode customer subdomains
- Credentials: Use User Secrets (`dotnet user-secrets`) or `.env`

### For Test Fixtures

Use clearly fake/generic data:
```csharp
private const string TestSessionId = "00000000-0000-0000-0000-000000000001";
private const int TestEmployeeId = 12345;
```

This prevents accidental exposure of personal/customer information in the repository.
