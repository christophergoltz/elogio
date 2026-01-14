# Absences API - Reverse Engineering

This document describes the Kelio BWP API for retrieving absence/leave data (vacation, sick leave, public holidays, etc.).

## API Endpoint

**Service:** `com.bodet.bwt.gtp.serveur.service.intranet.calendrier_absence.CalendrierAbsenceSalarieBWTService`

**Method:** `getAbsencesEtJoursFeries`

## Request Structure

```
11,"com.bodet.bwt.core.type.communication.BWPRequest",
"java.util.List",
"java.lang.Integer",
"com.bodet.bwt.core.type.time.BDate",
"com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceConfigurationBWT",
"java.lang.Boolean",
"NULL",
"java.lang.String",
"{SESSION_ID}",
"getAbsencesEtJoursFeries",
"com.bodet.bwt.gtp.serveur.service.intranet.calendrier_absence.CalendrierAbsenceSalarieBWTService",
0,1,5,
2,{EMPLOYEE_ID},
3,{START_DATE},    // Format: YYYYMMDD (e.g., 20250101)
3,{END_DATE},      // Format: YYYYMMDD (e.g., 20260331)
4,5,1,5,0,5,1,5,1,5,1,5,1,6,6,5,1,6,6,
2,3,
2,{REQUEST_ID},
7,8,7,9,7,10
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `SESSION_ID` | String (UUID) | Session ID from `GlobalBWTService.connect()` |
| `EMPLOYEE_ID` | Integer | Employee ID from `GlobalBWTService.connect()` response |
| `START_DATE` | Integer | Start date in YYYYMMDD format |
| `END_DATE` | Integer | End date in YYYYMMDD format |
| `REQUEST_ID` | Integer | Incremental request counter |

### Configuration Flags (CalendrierAbsenceConfigurationBWT)

The sequence `4,5,1,5,0,5,1,5,1,5,1,5,1,6,6,5,1,6,6` contains boolean configuration flags:
- Show school holidays
- Show week numbers
- Selected vacation zones
- Default month
- Telecommuting filter

## Response Structure

### Main Response Types (String Table)

| Index | Type | Description |
|-------|------|-------------|
| 3 | `CalendrierDemandesDataBWT` | Main container for absence data |
| 6 | `BDate` | Date type |
| 7 | `CalendrierDemandeJourDataBWT` | Day data container |
| 8 | `CalendrierAbsenceCellBWT` | Absence cell with color and type |
| 11 | `BColor` | Color type |
| 36/37 | `LegendeCalendrierBWT` | Legend container |
| 39 | `LegendeMotifBWT` | Legend entry for absence type |

### Day Entry Structure

Each day in the response follows this pattern:

```
5,{DATE},6,{DAY_DATA},3,{IS_HOLIDAY},3,{IS_WEEKEND},3,{IS_REST_DAY}
```

**Without absence:**
```
5,20260115,6,1,1,1,3,0,3,0,3,0,1,1,1
```
- `3,0,3,0,3,0` = Not a holiday, not weekend, not rest day

**With absence (e.g., vacation):**
```
5,20260430,6,1,7,8,{MOTIF_IDX},10,{COLOR},1,11,1,3,0,11,0,3,1,8,{SECONDARY_MOTIF},13,14,0,1,3,0,3,0,3,0
```
- `7` = CalendrierDemandeJourDataBWT marker
- `8` = CalendrierAbsenceCellBWT marker
- `{MOTIF_IDX}` = Index to motif ID string (request-specific identifier)
- `{COLOR}` = Color integer determining absence type
- `{SECONDARY_MOTIF}` = Secondary motif reference

**Holiday:**
```
5,20260101,6,1,1,1,3,1,3,0,3,0,1,1,1
```
- `3,1,3,0,3,0` = Is holiday, not weekend, not rest day

**Weekend:**
```
5,20260104,6,1,1,1,3,0,3,1,3,1,1,1,1
```
- `3,0,3,1,3,1` = Not holiday, is weekend, is rest day

## Color Mapping (Absence Types)

The absence type is determined by the **color value**, not by the motif string.

| Color (Integer) | Hex | RGB | Absence Type |
|-----------------|-----|-----|--------------|
| `-65536` | `0xFFFF0000` | Red (255,0,0) | **Sick Leave** (Krank mit/ohne AU) |
| `-16776961` | `0xFF0000FF` | Blue (0,0,255) | **Vacation** (Urlaub) |
| `-16711808` | `0xFF00FF80` | Green (0,128,0) | **Private Appointment** (Privat) |
| `-256` | `0xFFFFFF00` | Yellow (255,255,0) | **Half Holiday** (Halber Feiertag) |
| `-3355444` | `0xFFCCCCCC` | Gray (204,204,204) | **Rest Day** (Ruhezeit) |

### Color Decoding (Java Integer to RGB)

```csharp
int colorInt = -16776961;
byte r = (byte)((colorInt >> 16) & 0xFF);
byte g = (byte)((colorInt >> 8) & 0xFF);
byte b = (byte)(colorInt & 0xFF);
// Result: RGB(0, 0, 255) = Blue
```

## Legend Structure

The legend is located at the end of the response and maps colors to display labels:

```
37,0,3,1,3,0,3,0,
38,8,39,10,-65536,8,40,13,14,0,      // Red -> "Feiertag" (public holiday)
37,5,
38,8,12,10,-16711808,8,41,13,14,0,   // Green -> "Privat"
38,8,28,10,-16776961,8,42,13,14,0,   // Blue -> "Urlaub"
38,8,19,10,-65536,8,43,13,14,0,      // Red -> "Krank mit AU"
38,8,25,10,-65536,8,44,13,14,0,      // Red -> "Krank ohne AU"
38,8,17,10,-256,8,45,13,14,0,        // Yellow -> "Halber Feiertag"
46,0,
38,8,39,10,-3355444,8,47,13,14,0,    // Gray -> "Ruhezeit"
```

### Legend Entry Format

```
38,8,{MOTIF_TYPE_IDX},10,{COLOR},8,{LABEL_IDX},13,14,0
```

- `38` = Array marker for LegendeMotifBWT
- `8` = CalendrierAbsenceCellBWT type
- `{MOTIF_TYPE_IDX}` = Index to motif type identifier
- `{COLOR}` = Color integer
- `{LABEL_IDX}` = Index to display label string

## Motif Strings

The motif strings (like "URL", "KRK2", "364", "439") are **request/application IDs**, not type identifiers. Each absence request gets a unique ID. Examples:

| Motif String | Meaning |
|--------------|---------|
| `URL` | Vacation request ID |
| `KRK1` | Sick leave request ID (without certificate) |
| `KRK2` | Sick leave request ID (with certificate) |
| `HFT` | Half-day holiday |
| `Priv` | Private appointment |
| `364`, `439`, etc. | Numeric request IDs |

## Example: Parsing Day Data

### Regular Work Day
```
5,20260115,6,1,1,1,3,0,3,0,3,0,1,1,1
```
- Date: 2026-01-15
- No absence
- Not a holiday (`3,0`)
- Not a weekend (`3,0`)
- Not a rest day (`3,0`)

### Vacation Day
```
5,20260430,6,1,7,8,19,10,-16776961,1,11,1,3,0,11,0,3,1,8,18,13,14,0,1,3,0,3,0,3,0
```
- Date: 2026-04-30
- Has absence data (`7` marker)
- Absence cell (`8` marker)
- Color: `-16776961` = Blue = **Vacation**
- Not a holiday, not weekend

### Sick Leave Day
```
5,20250714,6,1,7,8,32,10,-65536,1,11,1,3,0,11,0,3,1,8,19,13,14,0,1,3,0,3,0,3,0
```
- Date: 2025-07-14
- Color: `-65536` = Red = **Sick Leave**

### Public Holiday
```
5,20260101,6,1,1,1,3,1,3,0,3,0,1,1,1
```
- Date: 2026-01-01 (New Year)
- Is holiday (`3,1`)
- No color (pure holiday without absence request)

### Half Holiday
```
5,20261224,6,7,8,16,10,-256,1,11,1,3,0,11,0,3,1,8,12,13,14,0,1,1,3,0,3,0,3,0
```
- Date: 2026-12-24 (Christmas Eve)
- Color: `-256` = Yellow = **Half Holiday**

### Private Appointment
```
5,20250612,6,1,1,7,8,21,10,-16711808,1,11,1,3,0,11,1,3,1,8,12,13,14,0,3,0,3,0,3,0
```
- Date: 2025-06-12
- Color: `-16711808` = Green = **Private Appointment**

## Implementation Notes

1. **Color is the type identifier**: Do not rely on motif strings for type detection. Use the color value.

2. **Boolean flags for special days**: After the absence data, each day has three boolean flags:
   - `isHoliday` (3,1 = true, 3,0 = false)
   - `isWeekend` (3,1 = true, 3,0 = false)
   - `isRestDay` (3,1 = true, 3,0 = false)

3. **Overlapping types**: A day can be both a holiday AND have an absence (e.g., 01.05.2026 is Labor Day but user also has vacation).

4. **Date range**: The API typically returns 15 months of data (e.g., 2025-01-01 to 2026-03-31).

5. **Employee-specific**: Results are filtered to the authenticated employee's data only.
