# Elogio - Roadmap

## 1. Abgegoltene Überstunden

- [ ] API-Endpunkt für automatisch abgegoltene Überstunden finden
- [ ] Konfigurierbare Anzahl (z.B. 10h/Monat) ermitteln
- [ ] Anzeige der abgegoltenen vs. tatsächlichen Überstunden
- [ ] Restliche anrechenbare Überstunden berechnen

## 2. Verbesserungen Monatsansicht

- [ ] Zeitbereich für "Privat"-Abwesenheiten anzeigen (z.B. "Privat (08:00 - 13:30)")
  - Benötigt ggf. andere API oder Kombination aus Absence + Presence API

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

- **DashboardViewModel Größe (~525 Zeilen):** Kohäsiv, alle Properties gehören zum Dashboard. Wird bei Feature-Erweiterung überprüft.
- **KelioService Größe (713 Zeilen):** Cache-Logik ist stark mit Service verbunden. Extraktion nur bei signifikantem Wachstum.
- **Hardcoded Day Names ("Mo", "Di", etc.):** Deutsche App, Lokalisierung aktuell nicht geplant.
