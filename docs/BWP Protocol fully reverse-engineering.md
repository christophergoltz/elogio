# Kelio API Research Findings

## Overview

**Target:** https://example.kelio.io/open/login
**Product:** Kelio - Workforce Management / Time & Attendance System
**Vendor:** Bodet Software
**Research Date:** 2026-01-08
**Status:** ✅ BWP Protocol fully reverse-engineered

---

## Technology Stack

### Frontend
- **Framework:** GWT (Google Web Toolkit)
- **Bootstrap Files:**
  - `portail.nocache.js` → `85D2B992F6111BC9BF615C4D657B05CC.cache.js`
  - `app_declaration_desktop.nocache.js` → `1A313ED29AA1E74DD777D2CCF3248188.cache.js`
- **Apps identified:**
  - `portail` - Main portal
  - `app_declaration_desktop` - Time declaration / punch clock

### Backend
- **Server:** Apache
- **Framework:** Spring Security (login via `j_spring_security_check`)
- **Protocol:** BWP (Bodet Web Protocol) - proprietary GWT RPC variant
- **Content-Type:** `text/bwp;charset=UTF-8`

---

## Authentication Flow

### 1. Login Page
```
GET https://example.kelio.io/open/login
```
Returns HTML form with:
- `username` field
- `password` field
- `_csrf_bodet` hidden field (CSRF token)
- `ACTION` hidden field

### 2. Login Submit
```
POST https://example.kelio.io/open/j_spring_security_check
Content-Type: application/x-www-form-urlencoded

ACTION=ACTION_VALIDER_LOGIN
username=<username>
password=<password>
_csrf_bodet=<csrf_token>
```

### 3. Post-Login Redirect
```
GET https://example.kelio.io/open/homepage
→ Redirects to /open/bwt/portail.jsp
```

### 4. Session Tokens
- **Cookies:** Session cookies set after login
- **Headers:**
  - `x-csrf-token` - CSRF token for API calls
  - `x-kelio-stat` - Statistics/timing header

---

## API Architecture

### Main Endpoint
```
POST https://example.kelio.io/open/bwpDispatchServlet?<timestamp>
Content-Type: text/bwp;charset=UTF-8
```

### Push/Real-time System
```
GET  /open/push/connect?<timestamp>           → Returns session ID (e.g., "291")
GET  /open/push/listen?id=<id>&<timestamp>    → Long-polling listener
POST /open/push/subscribe/<id>/<protocol>     → Subscribe to updates
POST /open/push/unsubscribe?id=<id>&subscriberId=<sid>
GET  /open/push/disconnect?id=<id>
```

### Push Protocols Identified
- `AbsencesDuJourBWTPushProtocol` - Today's absences
- `IndicateurPortailBWTPushProtocol` - Portal indicators
- `AlertePortailBWTPushProtocol` - Alerts
- `NotificationPortailBWTPushProtocol` - Notifications

### App Launcher
```
GET /open/bwt/appLauncher.jsp?app=<app_name>&appParams=<params>
```
Example:
```
/open/bwt/appLauncher.jsp?app=app_declaration_desktop&appParams=idMenuDeclaration=1
```

---

## BWP Protocol Analysis

### ✅ SOLVED - BWP Encoding Algorithm

The BWP (Bodet Web Protocol) has been fully reverse-engineered from the GWT JavaScript.

#### Format Structure
```
[MARKER][KEY_COUNT][KEYS...][ENCODED_BODY]
```

| Component | Description |
|-----------|-------------|
| MARKER | `0xA4` (¤ character) |
| KEY_COUNT | `chr(48 + N)` where N = number of keys (4-37) |
| KEYS | `chr(48 + key[i] + (i % 11))` for each key (values 0-14) |
| BODY | Each char: `chr(charCode + key[i % N] - (i % 17))` |

#### Decoding Algorithm
```python
# 1. Check marker
if ord(data[0]) != 0xA4:
    return data  # Not BWP encoded

# 2. Read key count
key_count = ord(data[1]) - 48

# 3. Read keys
keys = []
for i in range(key_count):
    key = ord(data[2 + i]) - 48 - (i % 11)
    keys.append(key)

# 4. Decode body
header_length = 2 + key_count
decoded = []
for i, char in enumerate(data[header_length:]):
    decoded_code = ord(char) - keys[i % len(keys)] + (i % 17)
    decoded.append(chr(decoded_code & 0xFFFF))
```

#### Encoding Algorithm
```python
# 1. Generate keys
key_count = random.randint(4, 37)
keys = [random.randint(0, 14) for _ in range(key_count)]

# 2. Build header
result = chr(0xA4)  # Marker
result += chr(48 + len(keys))  # Key count
for i, key in enumerate(keys):
    result += chr(48 + key + (i % 11))  # Encoded keys

# 3. Encode body
for i, char in enumerate(data):
    encoded = ord(char) + keys[i % len(keys)] - (i % 17)
    result += chr(encoded & 0xFFFF)
```

#### Example Decoded Request
```
7,"com.bodet.bwt.core.type.communication.BWPRequest",
"java.util.List","java.lang.Integer","java.lang.String",
"3bb93284-0ac5-4db7-b899-8ca54126c981",
"getHeureServeur",
"com.bodet.bwt.global.serveur.service.GlobalBWTService",
0,1,0,2,226,3,4,3,5,3,6
```

### Java Classes Identified
From GWT RPC requests:
- `com.bodet.bwt.core.type.communication.BWPRequest`
- `com.bodet.bwt.core.type.communication.BWPResponse`
- `com.bodet.bwt.portail.serveur.domain.ParamPortailBWT`
- `com.bodet.bwt.portail.serveur.domain.BApplicationParametersPortail`
- `com.bodet.bwt.portail.serveur.domain.commun.TargetBWT`
- `com.bodet.bwt.portail.serveur.service.exec.PortailBWTService`

### Methods Identified
- `connect` - Initial connection to portal service

---

## GWT Cache Files

### Portal Module
- **Bootstrap:** `/open/bwt/portail/portail.nocache.js`
- **Compiled:** `/open/bwt/portail/85D2B992F6111BC9BF615C4D657B05CC.cache.js`
- **Size:** Large (contains all serialization logic)

### Declaration App Module
- **Bootstrap:** `/open/bwt/app_declaration_desktop/app_declaration_desktop.nocache.js`
- **Compiled:** `/open/bwt/app_declaration_desktop/1A313ED29AA1E74DD777D2CCF3248188.cache.js`
- **Purpose:** Time punch / declaration functionality

### Access Requirements
- Files require authentication (401 without session)
- Must be downloaded with valid session cookies

---

## Security Headers

```
Content-Security-Policy: default-src 'self'; script-src 'strict-dynamic' 'nonce-xxx'...
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
X-Content-Type-Options: nosniff
X-Frame-Options: SAMEORIGIN
X-XSS-Protection: 1; mode=block
X-Robots-Tag: noindex, nofollow, noarchive, nosnippet, noimageindex, noodp
```

---

## Recommended Approaches

### Option A: Browser Automation (Simplest)
- Use Playwright to automate browser
- Parse rendered DOM
- Pros: Works immediately, no reverse engineering needed
- Cons: Heavy dependency (~200MB Chromium), slow

### Option B: Hybrid Approach (Recommended)
- Use Playwright only for login (get cookies)
- Use HTTP requests for data fetching
- Parse HTML pages (not BWP API)
- Pros: Faster, smaller footprint after login
- Cons: Limited to HTML-accessible data

### Option C: Full API Reverse Engineering (Most Complex)
- Decompile GWT cache.js files
- Understand BWP serialization format
- Implement BWP encoder/decoder
- Pros: Cleanest CLI, direct API access, fastest
- Cons: Significant reverse engineering effort

---

## Files in This Repository

| File | Purpose |
|------|---------|
| `bwp_codec.py` | ✅ **BWP Encoder/Decoder** - Main codec |
| `discover_api.py` | Playwright-based login + API discovery |
| `discover_api.py --quick` | Quick mode: Login + export cookies only |
| `download_gwt.py` | Download GWT JS files (with auth) |
| `analyze_bwp.py` | BWP protocol analysis (obsolete) |
| `decode_bwp.py` | BWP encoding analysis (obsolete) |
| `api_discovery.json` | 67 captured API requests/responses |
| `cookies.json` | Session cookies for API calls |
| `.env.example` | Template for credentials |
| `gwt_files/` | Downloaded GWT JavaScript files |

---

## Next Steps

### ✅ Completed
1. ~~Reverse-engineer BWP protocol~~ → `bwp_codec.py`
2. ~~Download GWT cache files~~ → `gwt_files/`
3. ~~Implement encoder/decoder~~ → Working!

### TODO: Build API Client
1. **GWT-RPC Parser** - Parse the decoded comma-separated GWT format
2. **API Client** - Make direct HTTP calls with `requests` + cookies + BWP codec
3. **Data Extraction** - Extract timestamps, absences, etc. from responses

### Known API Methods (from decoded requests)
- `connect` - Initial portal connection
- `getHeureServeur` - Get server time
- `GlobalBWTService` - Global service calls
- `PortailBWTService` - Portal service calls

### Usage Example
```python
from bwp_codec import BWPCodec

codec = BWPCodec()

# Decode BWP → GWT-RPC
msg = codec.decode(bwp_encoded_string)
print(msg.decoded)  # '7,"com.bodet.bwt..."'

# Encode GWT-RPC → BWP
bwp = codec.encode(gwt_rpc_string)
```

---

## TLS Fingerprinting Issue

### Problem
The Kelio server uses TLS fingerprinting (JA3/JA4) to detect non-browser HTTP clients.
Standard HTTP clients (like .NET HttpClient, Python requests) get **401 Unauthorized** for
BWP-encoded requests, even with valid session cookies.

### Solution: curl_cffi
We use `curl_cffi` (Python library) with `impersonate="chrome120"` to match Chrome's
TLS fingerprint. This is wrapped in `curl_proxy.py` for subprocess calls from C#.

**Key insight:** When calling curl_proxy.py from C#, the body must be passed using:
```csharp
Process.StartInfo.Arguments = $"python curl_proxy.py POST url --body \"{escapedBody}\"";
Process.StartInfo.UseShellExecute = false;  // CRITICAL - no shell interpretation
```

With Python subprocess, use:
```python
subprocess.run(command_string, shell=False, ...)  # shell=False with string
```

Using `shell=True` causes BWP-encoded characters (like 0xA4) to be interpreted as shell commands!

---

## ✅ SOLVED: ExceptionBWT Issue (2026-01-12)

### Root Causes Identified

The `ExceptionBWT` error had **three root causes**:

#### 1. Dynamic Employee ID (CRITICAL)
The employee ID is **session-specific** and must be extracted from the `GlobalBWTService connect` response.

- **Old capture (Jan 8):** Employee ID 227
- **New capture (Jan 12):** Employee ID 574, 589, 591, 595 (changes per session!)

The employee ID appears at the end of the GlobalBWTService connect response, before the user's name:
```
[..., TYPE_REF, EMPLOYEE_ID, TYPE_REF, FIRSTNAME_IDX, TYPE_REF, LASTNAME_IDX, ...]
Example: [..., 1086, 574, 4, 1139, 4, 1140, 1]
```

#### 2. GlobalBWTService Connect Format
The `GlobalBWTService connect` request was missing required parameters:

**Wrong:**
```
0,1,2,2,3,-{ts},4,5,6,5,7,5,8
```

**Correct:**
```
0,1,2,2,21,3,411,-{ts},4,5,6,5,7,5,8
```

The browser sends:
- Short value: `21`
- Long values: `411` (device type?) and `-{timestamp}`

#### 3. BWP Body Encoding via Subprocess
When passing BWP-encoded data through subprocess command line, special characters (like 0xA4) get corrupted.

**Solution:** Write BWP body to a temp file and use `--body-file` argument:
```python
# Instead of --body "encoded_data"
# Use --body-file "temp_file.txt"
```

### Working Request Sequence

```
1. Login (GET /open/login, POST /open/j_spring_security_check)
2. Portal (GET /open/bwt/portail.jsp) → Extract session_id from csrf_token div
3. PortailBWTService connect (raw GWT-RPC)
4. Push connect (GET /open/push/connect)
5. AppLauncher (GET /open/bwt/appLauncher.jsp)
6. GlobalBWTService connect (raw GWT-RPC) → Extract employee_id from response
7. getTraductions (BWP-encoded, using extracted employee_id)
8. getSemaine (BWP-encoded, using extracted employee_id) → SUCCESS!
```

### Test Script
See `test_dynamic_employee_id.py` for working implementation.

---
