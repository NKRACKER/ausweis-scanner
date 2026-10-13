# Ausweis-Scanner

Portables Windows-Tool: **mehrere Schülerausweise auf einmal scannen** → jeder Ausweis wird ausgeschnitten, gerade gedreht,
der Name per Texterkennung vorgeschlagen (du bestätigst) und als eigene Datei **`Nachname_Vorname.jpg`** gespeichert (oder PDF/PNG).

- **Ohne Installation, ohne Admin-Rechte:** eine einzelne `.exe` (rund 0,8 MB), nur Windows 10/11 (.NET Framework 4.8 ist dort eingebaut).
- **Datenschutz:** alles lokal. Keine Telemetrie, keine Cloud-OCR. Der Rohscan bleibt nur im Arbeitsspeicher, gespeichert werden nur die einzelnen Ausweise. Einstellungen/Designs enthalten keine Namen. Einzige Internet-Verbindung: eine anonyme Abfrage bei github.com, ob es eine neue Version gibt (abschaltbar, sendet keine Daten über Scans/Ausweise).
- **Beliebiger Scanner:** WIA, TWAIN, Netzwerkscanner (AirScan/eSCL) oder einfach eine fertige Scan-Datei (Bild/PDF) – auch per Ordnerüberwachung.
- **Design-fest:** Ausweise werden nur an Form/Kontrast erkannt; der Name wird per Profil, Beschriftung („Name:“, „Vorname“) oder Schätzung gefunden. Neues Ausweis-Design = in 20 Sekunden anlernen.

**Download:** GitHub → [Releases](https://github.com/NKRACKER/ausweis-scanner/releases) (`Ausweis-Scanner-x64.exe`, bei Bedarf `…-x86.exe`).

**Updates:** Gibt es eine neuere Version, erscheint oben rechts ein grüner Knopf „⬆ Update vX.Y.Z verfügbar“. Ein Klick lädt das Programm herunter, prüft die SHA-256-Prüfsumme gegen das Release (nur bei Übereinstimmung wird installiert), ersetzt die eigene `.exe` und startet neu. Ist der Ordner schreibgeschützt (z. B. Stick im Schreibschutz), läuft die neue Version aus dem Datenordner. Die Suche ist ein **Schalter** (Einstellungen → Allgemein → Updates): aus = das Programm baut von sich aus nie eine Internetverbindung auf. *Veröffentlichen:* Tag `vX.Y.Z` pushen → die CI baut, prüft und erstellt das Release.

## Benutzung (drei Schritte)

1. **Auflegen:** Ausweise mit der Bildseite **nach unten** auf die Glasplatte, **~5 mm Abstand** zueinander und zum Rand. Drehung egal. Deckel offen lassen oder dunkles Papier darüberlegen (erkennt helle Karten am zuverlässigsten).
2. **Scannen:** großer Knopf „Scannen“ – oder eine fertige Scan-Datei (Bild/PDF) ins Fenster ziehen bzw. „Datei öffnen…“. Der Scanner wird automatisch gewählt.
3. **Prüfen & speichern:** Jeder erkannte Ausweis ist eine Kachel mit farbigem Streifen (grün = erkannt, orange = bitte prüfen, rot = nichts erkannt). **Enter** springt zum nächsten Feld. An der Kachel: `⇄` tauscht Vor-/Nachname, `⟳` dreht, `✕` entfernt, `⋯` zeigt andere Namensvorschläge. Dann **„Alle speichern“** (Strg+S). Es wird nie überschrieben: doppelte Namen bekommen `_2`, `_3` …

Mit „＋ Weiterer Scan“ lassen sich mehrere Scans sammeln und gemeinsam speichern.

**Gesamten Scan prüfen:** Über „Gesamten Scan ansehen“ erscheint der komplette Scan mit allen erkannten Rahmen (grün, nummeriert). Ein Klick auf einen Ausweis nimmt ihn heraus (grau, ✕) bzw. wieder hinein. Wurde ein Ausweis nicht erkannt, zieht man mit der Maus **grob** einen Rahmen darum – die genauen Ecken (auch bei schräg liegenden Ausweisen) werden automatisch gesucht. Am gewählten Ausweis lassen sich die Eckpunkte ziehen (Größe anpassen; mit Strg gedrückt dreht der Ausweis), ein Zug im Ausweis verschiebt ihn. Falsche oder überflüssige Rahmen lassen sich löschen (gewählten Rahmen mit **Entf**, per Rechtsklick oder über „Rahmen löschen“; eine dazugehörige Kachel verschwindet dabei auch aus der Liste). **Zuschnitt nachträglich korrigieren:** An jeder Kachel öffnet ✂ (oder ⋯ → „Zuschnitt anpassen“, auch in der Großansicht) den Gesamtscan mit diesem Ausweis schon gewählt – Eckpunkte ziehen, „Übernehmen“. Ein weicher Scanner-Schatten am Kartenrand wird beim automatischen Ausschneiden schon abgeschnitten. Mit „Übernehmen“ werden neue oder veränderte Ausweise ausgeschnitten und der Name gelesen. Wird gar nichts erkannt, öffnet sich diese Ansicht automatisch. Der Scan bleibt dafür nur im Arbeitsspeicher, bis die Liste geleert oder gespeichert ist.

**Alles Weitere liegt unter „Einstellungen“** (Tabs): *Allgemein* (Ordner, Format, Dateiname), *Scanner* (Gerät, Auflösung, Hersteller-Dialog, Netzwerk), *Ausweis-Design* (aktives Design, neues anlernen, löschen), *Erweitert* (JPEG-Qualität, Randabzug, Ordnerüberwachung), *Diagnose*.

## Scanner-Anbindung (wird automatisch kombiniert, du wählst nur das Gerät)

| Weg | Wann | Hinweis |
|---|---|---|
| **WIA** | USB-/Netzwerkscanner mit Windows-Treiber (häufigster Fall) | „Scannen“ = Schnellscan der ganzen Glasplatte; in den Einstellungen (Scanner) lässt sich stattdessen die Oberfläche des Herstellers einschalten |
| **TWAIN** | ältere/professionelle Scanner ohne WIA | Hersteller-Treiber nötig; TWAIN-Manager ist eingebaut. Bei alten 32-Bit-Treibern die `x86`-Variante nehmen. *Ohne Gerät nicht testbar → experimentell* |
| **Netzwerk (AirScan/eSCL)** | WLAN-/Netzwerkscanner ohne Treiber | nur auf Wunsch (Einstellungen), spricht ausschließlich mit der gewählten Scanner-Adresse im lokalen Netz |
| **Datei / Ordner** | immer | Bild (JPG/PNG/BMP/TIFF) oder PDF mit eingebettetem Scan; Ordnerüberwachung in den Einstellungen |

Geht nichts davon: mit der Scanner-Software (oder der Windows-Scan-App) scannen und die Datei hineinziehen.

## Wenn sich das Ausweis-Design ändert

Das Tool braucht kein festes Layout. Die Namenssuche läuft in drei Stufen, jeweils mit Plausibilitätsprüfung:

1. **Profil** (gelernte Felder, siehe unten) – am genauesten.
2. **Beschriftungen:** „Name: Mustermann, Max“ oder „Nachname … / Vorname …“ (auch über dem Wert, auch mit OCR-Tippfehlern).
3. **Schätzung:** größte namensähnliche Zeile (Stoppwörter wie „Schülerausweis“, „Gymnasium“, „Gültig bis“ werden ignoriert) – wird immer als „unsicher“ markiert.

Die Auswahl **„Automatisch“** probiert alle gespeicherten Designs durch und merkt sich das passende. Passt kein Profil, fällt es auf Stufe 2/3 zurück – es endet also nie bei „leer“, und von Hand eintippen geht immer.

**Neues Design anlernen:** Einstellungen → *Ausweis-Design* → `Neues Design anlernen…` → Beispielkarte (oder schon gescannte Karte) → mit der Maus einen Rahmen um das Namensfeld ziehen (oder getrennt Nachname/Vorname) → `Testen` → speichern. Das Design liegt als JSON im Ordner `profile` (neben der `.exe` in `Ausweis-Scanner-Daten`, sonst unter `%LOCALAPPDATA%\Ausweis-Scanner`) und kann von Hand bearbeitet oder zwischen Rechnern kopiert werden:

```json
{
  "name": "Schule XY 2027",
  "valid_from": "2027-08-01",
  "card_mm": [85.6, 54],
  "orientation": "landscape",
  "name_format": "LastCommaFirst",
  "fields": [ { "role": "name", "x": 0.32, "y": 0.36, "w": 0.62, "h": 0.14, "strip": ["Name:"] } ],
  "stopwords": ["Humboldt"]
}
```
`x/y/w/h` sind Anteile der Karte (0–1, Querformat, links oben). `role`: `name` (beides in einem Feld) | `last` | `first`. `name_format`: `Auto` | `LastCommaFirst` („Müller, Anna“) | `FirstLast` („Anna Müller“) | `LastFirst` („MÜLLER Anna“).

Der **Dateiname** ist anpassbar (Einstellungen): `{Nachname}_{Vorname}` (Standard), auch `{Klasse}` und `{Datum}`.

## Texterkennung

Nutzt die eingebaute Windows-Texterkennung (offline). Für Umlaute ist das **Deutsch-Sprachpaket mit „Optische Zeichenerkennung“** am besten (Einstellungen → Zeit & Sprache → Sprache → Deutsch → Sprachoptionen; braucht Adminrechte). Fehlt es, funktioniert die Erkennung mit Englisch eingeschränkt; fehlt jede Sprache, werden die Namen von Hand eingetippt – alles andere läuft weiter.

## Diagnose & Kommandozeile

Einstellungen → *Diagnose* zeigt gefundene Scanner, OCR-Sprachen und TWAIN-Status (ohne Personendaten, zum Weitergeben gedacht). Automatisierung:

```
Ausweis-Scanner.exe --process <Datei|Ordner> --out <Ordner> [--format jpg|pdf|png] [--profile <Name>] [--report <json>]
Ausweis-Scanner.exe --diag [<Datei>]
Ausweis-Scanner.exe --make-testscan <Ordner> [--dark] [--umlauts]   # synthetischer Beispiel-Scan mit Fantasie-Ausweisen
```

## Technik

```
src/Core   netstandard2.0, ohne Windows-API → unter Linux testbar: Kartenerkennung, Namenslogik, Profile, Dateiformate (PNG/PDF), eSCL, mDNS
src/App    net48 WinForms: WIA/TWAIN/eSCL-Backends, Windows-OCR (per Reflection), UI, CLI
tests      xUnit-Tests (Linux), inkl. synthetischer Scans (heller/dunkler Deckel, gedreht, Staub, sich berührende Karten) und Mock-eSCL-Scanner
```

- **Build unter Linux:** `dotnet test tests/Core.Tests` · `dotnet build src/App -c Release -p:Platform=x64` (Cross-Build der Windows-.exe; Abhängigkeiten werden in die .exe eingebettet).
- **CI (GitHub Actions, `windows-latest`):** baut x64+x86, installiert das deutsche OCR-Paket, erzeugt synthetische Scans (3 Designs, 6 Karten, gedreht/kopfüber/quer, hell+dunkel) und prüft per echter Windows-OCR, dass genau die erwarteten Dateinamen entstehen; rendert die Oberfläche als Screenshot.
- **Drittkomponenten:** siehe `THIRD-PARTY-NOTICES.md` (TWAIN-DSM, NTwain).

## Bekannte Grenzen

- **Nicht mit echter Hardware getestet:** WIA/TWAIN/AirScan gegen echte Scanner und die Erkennung echter Schülerausweise (Hologramme, Folienglanz, Muster). Deshalb gibt es überall Treiber-Dialog, Datei-Import und manuelle Eingabe als Rückfall.
- Karten, die sich berühren, werden nicht getrennt (Warnung „mit Abstand auflegen“).
- Unsignierte `.exe`: SmartScreen/Virenscanner können warnen oder blockieren; auf streng gesperrten Schul-PCs ggf. nicht startbar.
- PDF-Import nur für Scanner-PDFs mit eingebettetem JPEG/Flate-RGB-Bild.

## Mitmachen & Lizenz

Das Projekt steht unter der **MIT-Lizenz** (siehe `LICENSE`): Jeder darf es nutzen, ändern, weitergeben und weiterentwickeln. Fehlermeldungen und Pull Requests sind willkommen. Für Fehler ohne Scanner/Ausweise zur Hand hilft der Bericht unter Einstellungen → Diagnose (enthält keine Personendaten). Drittkomponenten: `THIRD-PARTY-NOTICES.md`.
