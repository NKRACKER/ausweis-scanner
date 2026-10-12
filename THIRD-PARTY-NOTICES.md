# Drittanbieter-Komponenten

| Komponente | Zweck | Lizenz | Quelle |
|---|---|---|---|
| **TWAIN Data Source Manager 2.5.1** (`src/App/native/twaindsm-x64.dll`, `twaindsm-x86.dll`) | Ansprechen von TWAIN-Scannern ohne Hersteller-Treiberpaket. Unverändert, von der TWAIN Working Group signiert; wird nur geladen, wenn Windows keine eigene `twaindsm.dll` mitbringt. | LGPL 2.1 | https://github.com/twain/twain-dsm/releases/tag/v2.5.1 (`dsm_020501.zip`) |
| **NTwain 3.7.6** | .NET-Anbindung an TWAIN | MIT | https://github.com/soukoku/ntwain |

SHA-256 der mitgelieferten DSM-Dateien (zur Prüfung gegen das Release):
- x64: `25869f88a2bc76ded62a13aaea9f2084f0165bfdbbb7f46b9c3a43298938d467`
- x86: `cee9a6ff8026dde09eda9f892c57cc928d42e4a4d22bb740fe38ee6e91797a28`

Die LGPL erlaubt den Austausch der DLL: eine eigene `twaindsm.dll` neben die `.exe` legen – sie wird bevorzugt geladen.
