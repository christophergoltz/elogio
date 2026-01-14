# Elogio - Roadmap

## 1. Refactorings & Cleanup

- [x] Unused usings entfernen
- [x] Redundante null-checks entfernen
- [x] Unbenutztes `_authApi` Feld entfernen
- [x] Verbleibende Rider-Warnungen beheben (~62)
- [x] Fehlgeschlagenen Test fixen (`SemainePresenceParserTests.Parse_RealResponse_WeekendDaysHaveZeroExpected`)

## 2. Abwesenheiten in Monatsansicht

- [x] API-Endpunkt für Abwesenheiten identifizieren (Urlaub, Krankheit, etc.)
- [x] BWP-Request für Abwesenheiten reverse-engineeren
- [x] Abwesenheitsdaten abrufen und parsen
- [x] In Monatsansicht integrieren (visuelle Darstellung)

## 3. Verbesserungen Monatsansicht

- [ ] Zeitbereich für "Privat"-Abwesenheiten anzeigen (z.B. "Privat (08:00 - 13:30)")
  - Benötigt ggf. andere API oder Kombination aus Absence + Presence API
- [ ] Visuelle Darstellung für kombinierte Abwesenheiten (z.B. Urlaub + Halber Feiertag am 24.12.)

## 4. Jahresübersicht

- [ ] Jahresübersicht-View erstellen
- [ ] Abwesenheiten im Jahreskalender anzeigen
- [ ] Farbliche Kennzeichnung nach Abwesenheitstyp

## 5. Abgegoltene Überstunden

- [ ] API-Endpunkt für automatisch abgegoltene Überstunden finden
- [ ] Konfigurierbare Anzahl (z.B. 10h/Monat) ermitteln
- [ ] Anzeige der abgegoltenen vs. tatsächlichen Überstunden
- [ ] Restliche anrechenbare Überstunden berechnen

## 6. Abwesenheiten Kollegen

- [ ] API-Endpunkt für Team-/Kollegen-Abwesenheiten identifizieren
- [ ] Abwesenheiten anderer Mitarbeiter abrufen
- [ ] Anzeige "Wer ist heute abwesend?"
- [ ] Anzeige "Wann ist Person X wieder da?"

## 7. GitHub CI/CD Pipeline

- [x] GitHub Actions Workflow erstellen
- [x] Build & Test bei Push/PR
- [x] Release-Build bei Tag/Release

## 8. Automatische Client-Updates (Velopack)

- [x] Velopack NuGet-Paket integrieren
- [x] Update-Check beim App-Start implementieren
- [x] GitHub Releases als Update-Quelle konfigurieren
- [x] Installer/Setup mit Velopack erstellen
- [x] Delta-Updates aktivieren (nur Änderungen downloaden)

---

## Priorisierung

| Prio | Feature | Aufwand | Bemerkung |
|------|---------|---------|-----------|
| 1 | Refactorings & Cleanup | Gering | Technische Schulden reduzieren |
| 2 | GitHub CI/CD Pipeline | Gering | Früh einrichten für automatisierte Builds |
| 3 | Automatische Updates (Velopack) | Mittel | Ermöglicht einfache Releases |
| 4 | Abwesenheiten Monatsansicht | Mittel | Basis für weitere Features |
| 5 | Verbesserungen Monatsansicht | Gering | Privat-Zeitbereich, kombinierte Absences |
| 6 | Abgegoltene Überstunden | Mittel | Wichtig für Zeiterfassung |
| 7 | Jahresübersicht | Mittel | Aufbauend auf 4 |
| 8 | Kollegen-Abwesenheiten | Hoch | Ggf. andere API-Berechtigung nötig |

## Offene Fragen

- [x] Welche Abwesenheitstypen gibt es im Kelio-System?
  - Urlaub (Blau), Krankheit mit/ohne AU (Rot), Privattermin (Grün), Halber Feiertag (Gelb), Feiertag, Ruhezeit
- [ ] Gibt es eine API für Team-Abwesenheiten oder nur für den eigenen User?
- [ ] Wie werden abgegoltene Überstunden im System konfiguriert?
