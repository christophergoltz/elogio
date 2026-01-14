# Elogio - Roadmap

## 1. Refactorings & Cleanup

- [x] Unused usings entfernen
- [x] Redundante null-checks entfernen
- [x] Unbenutztes `_authApi` Feld entfernen
- [x] Verbleibende Rider-Warnungen beheben (~62)
- [x] Fehlgeschlagenen Test fixen (`SemainePresenceParserTests.Parse_RealResponse_WeekendDaysHaveZeroExpected`)

## 2. Abwesenheiten in Monatsansicht

- [ ] API-Endpunkt für Abwesenheiten identifizieren (Urlaub, Krankheit, etc.)
- [ ] BWP-Request für Abwesenheiten reverse-engineeren
- [ ] Abwesenheitsdaten abrufen und parsen
- [ ] In Monatsansicht integrieren (visuelle Darstellung)

## 3. Jahresübersicht

- [ ] Jahresübersicht-View erstellen
- [ ] Abwesenheiten im Jahreskalender anzeigen
- [ ] Farbliche Kennzeichnung nach Abwesenheitstyp

## 4. Abgegoltene Überstunden

- [ ] API-Endpunkt für automatisch abgegoltene Überstunden finden
- [ ] Konfigurierbare Anzahl (z.B. 10h/Monat) ermitteln
- [ ] Anzeige der abgegoltenen vs. tatsächlichen Überstunden
- [ ] Restliche anrechenbare Überstunden berechnen

## 5. Abwesenheiten Kollegen

- [ ] API-Endpunkt für Team-/Kollegen-Abwesenheiten identifizieren
- [ ] Abwesenheiten anderer Mitarbeiter abrufen
- [ ] Anzeige "Wer ist heute abwesend?"
- [ ] Anzeige "Wann ist Person X wieder da?"

## 6. GitHub CI/CD Pipeline

- [x] GitHub Actions Workflow erstellen
- [x] Build & Test bei Push/PR
- [x] Release-Build bei Tag/Release

## 7. Automatische Client-Updates (Velopack)

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
| 4 | Abwesenheiten Monatsansicht | Mittel | Basis für 5-7 |
| 5 | Abgegoltene Überstunden | Mittel | Wichtig für Zeiterfassung |
| 6 | Jahresübersicht | Mittel | Aufbauend auf 4 |
| 7 | Kollegen-Abwesenheiten | Hoch | Ggf. andere API-Berechtigung nötig |

## Offene Fragen

- [ ] Welche Abwesenheitstypen gibt es im Kelio-System?
- [ ] Gibt es eine API für Team-Abwesenheiten oder nur für den eigenen User?
- [ ] Wie werden abgegoltene Überstunden im System konfiguriert?
