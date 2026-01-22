# Elogio - Roadmap

## 1. Abgegoltene Überstunden

- [ ] API-Endpunkt für automatisch abgegoltene Überstunden finden
- [ ] Konfigurierbare Anzahl (z.B. 10h/Monat) ermitteln
- [ ] Anzeige der abgegoltenen vs. tatsächlichen Überstunden
- [ ] Restliche anrechenbare Überstunden berechnen

## 2. Abwesenheiten Kollegen

- [ ] API-Endpunkt für Team-/Kollegen-Abwesenheiten identifizieren
- [ ] Abwesenheiten anderer Mitarbeiter abrufen
- [ ] Anzeige "Wer ist heute abwesend?"
- [ ] Anzeige "Wann ist Person X wieder da?"

## 3. Verbesserungen Monatsansicht

- [ ] Zeitbereich für "Privat"-Abwesenheiten anzeigen (z.B. "Privat (08:00 - 13:30)")
  - Benötigt ggf. andere API oder Kombination aus Absence + Presence API

## 4. Jahresübersicht

- [ ] Jahresübersicht-View erstellen
- [ ] Abwesenheiten im Jahreskalender anzeigen
- [ ] Farbliche Kennzeichnung nach Abwesenheitstyp

---

## Offene Fragen

- [ ] Gibt es eine API für Team-Abwesenheiten oder nur für den eigenen User?
- [ ] Wie werden abgegoltene Überstunden im System konfiguriert?

## Bugs

- [ ] Monatskalender, für die einzelnen Tage fehlen die Border
- [ ] HFT und ÜST -> Haben den gleich gelben Farbcode, somit werden sie im code gleich gehandhabt und dadurch passt die Berechnung nicht mehr
  - HFT sind Halbe Feiertag und ÜST ist ein Überstundenausgleich

---

## Technical Debt - Clean Code & SOLID (2026-01-22)

### P1 - Hohe Priorität (vor Production Release)

- [x] **Mehrere Klassen pro Datei auftrennen**
  - `MainViewModel.cs`: `UpdateCheckStatus`, `TimeEntryDisplayItem`, `ToastNotificationEventArgs`, `ToastType` → eigene Dateien in `ViewModels/Models/`
  - `DashboardViewModel.cs`: `DayOverviewItem`, `DayOverviewState`, `AbsentColleagueItem` → eigene Dateien
  - Konvention: 1 Klasse = 1 Datei (Ausnahme: eng zusammengehörige Helper-Records)

### P2 - Mittlere Priorität (Code Quality)

- [x] **TimeSpanFormatter Utility-Klasse erstellen**
  - Duplicate Code in `DayOverviewItem.FormatTime()` und `DashboardViewModel.FormatTimeSpan()`
  - Zentrale `TimeSpanFormatter` Klasse mit Methoden: `Format()`, `FormatWithSign()`
  - Ort: `Elogio/Utilities/TimeSpanFormatter.cs`

- [x] **Magic Colors als Konstanten definieren**
  - Betroffene Dateien: `MainViewModel.cs`, `DashboardViewModel.cs`, `DayOverviewItem.cs`
  - Konstanten-Klasse: `Elogio/Resources/AppColors.cs`
  - Farben: SuccessGreen, ErrorRed, WarningOrange, InfoBlue, NeutralGray

### P3 - Niedrige Priorität (Nice-to-have)

- [ ] **Converter durch Styles/Triggers ersetzen (optional)**
  - `BoolToBackgroundConverter` → DataTrigger für Weekend-Highlighting
  - `BoolToMarginConverter` → DataTrigger für Update-Banner-Margin
  - Vorteil: XAML-nativer Ansatz, weniger C#-Code
  - Nachteil: Trigger können bei komplexer Logik unübersichtlich werden
  - **Entscheidung:** Abwägen ob Converter-Ansatz beibehalten (konsistent, testbar)

- [ ] **Cache-Logik aus KelioService extrahieren (SRP)**
  - `MonthDataCache` Klasse für Month-Caching (Dictionary + Prefetch-Logik)
  - `AbsenceDataCache` Klasse für Absence-Caching (Dictionary + Range-Tracking)
  - Würde KelioService von 713 auf ~400 Zeilen reduzieren
  - **Abwägen:** Aktuell kohäsiv, Extraktion nur bei weiterem Wachstum

- [ ] **PunchService/PunchViewModel extrahieren (SRP)**
  - Punch-Logik aus DashboardViewModel in separaten Service
  - Ermöglicht Wiederverwendung (z.B. Quick-Punch in TitleBar)
  - **Abwägen:** Aktuell nur im Dashboard benötigt

### Akzeptierte Technical Debt (dokumentiert, aber bewusst nicht refactored)

- **DashboardViewModel Größe (663 Zeilen):** Kohäsiv, alle Properties gehören zum Dashboard. Wird bei Feature-Erweiterung überprüft.
- **KelioService Größe (713 Zeilen):** Cache-Logik ist stark mit Service verbunden. Extraktion nur bei signifikantem Wachstum.
- **Hardcoded Day Names ("Mo", "Di", etc.):** Deutsche App, Lokalisierung aktuell nicht geplant.

### Abgeschlossene Refactorings

- [x] BoolConverters.cs → Aufgetrennt in BoolConverters.cs, BrushConverters.cs, NavigationConverters.cs, NumericConverters.cs
- [x] KelioClient.cs (1617 → 535 Zeilen) → Extrahiert: KelioAuthenticator, CalendarAppInitializer, SessionContext, EmployeeIdExtractor, TranslationLoader