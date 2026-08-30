# MediaForge Jellyfin Connector

Filme und Serien direkt in Jellyfin suchen und anfragen. Administratoren können
Anfragen freigeben oder automatisch freigeben lassen. MediaForge übernimmt die
Downloads; neu freigegebene Serien werden zusätzlich dauerhaft in Autosync aufgenommen.

**Version: 0.5.0 · Jellyfin ab 10.11 · MediaForge 1.5.x / 1.6.x**

Das Projekt besteht aus einem Jellyfin-Plugin und einem MediaForge-Modul.
**Bei Updates immer zuerst das MediaForge-Modul, danach das Jellyfin-Plugin aktualisieren.**

## Funktionen

### Für Benutzer

- Quellenübergreifende Suche sowie Reihen für neue und beliebte Inhalte und Filme.
- Bibliotheksabgleich: nur fehlende Filme, Staffeln oder Episoden anfragen.
- Bei passenden Anfragen ebenfalls Interesse anmelden; bereits angefragte
  Episoden werden gemeinsam genutzt, zusätzliche Episoden separat geplant.
- Vollständige Serien über **Zukünftige Folgen abonnieren** dauerhaft beobachten,
  ohne einen Erstdownload auszulösen.
- Persönlicher Verlauf mit Freigabe, Downloadfortschritt und tatsächlicher
  Jellyfin-Verfügbarkeit. Autosync besitzt einen eigenen Status.
- Eigene noch ausstehende Beteiligungen zurückziehen, ohne andere Benutzer oder
  laufende Downloads zu entfernen.
- Persönliches Mitteilungsfach mit Ungelesen-Zähler und konfigurierbaren Kategorien.
  Neue Folgen werden standardmäßig täglich pro Serie zusammengefasst.
- Berechtigungsgeprüfte Links zum Öffnen verfügbarer Inhalte in Jellyfin.

### Für Administratoren

- Übersicht mit Zählern, Titel-/Benutzer-/Status-/Quellen-/Zeitraumfiltern und Seitennavigation.
- Mehrfachfreigabe und Mehrfachablehnung mit Ergebnissen pro Anfrage und eigenen Ablehnungsgründen.
- Benutzerregeln für Freigabemodus, maximale offene Anfragen und die Erlaubnis für Serien-Abos.
- Getrennte Aktionen für Autosync-Wiederholung, erneute Prüfung fehlender Inhalte
  und Abgleich unklarer Downloadübergaben.
- Diagnose für Verbindung, Versionen, Modul-Fähigkeiten und API-Berechtigungen.
- Gemeinsame Vorgänge mit sichtbaren Beteiligten; normale Benutzer sehen keine fremden Identitäten.

### Autosync und Zuverlässigkeit

- Neue manuelle und automatische Serienfreigaben richten genau ein passendes
  Autosync-Abo ein oder übernehmen ein vorhandenes. Filme erhalten kein Abo.
- Bestehende Pausen, Filter, Sprache, Provider und Zielordner bleiben unverändert.
  Bestätigte Abos werden nach manueller Löschung nicht automatisch neu angelegt.
- Die erste Autosync-Bestandsprüfung startet keinen zusätzlichen Download.
  Spätere Prüfungen folgen den MediaForge-Einstellungen.
- Downloadübergabe und Autosync-Anmeldung werden getrennt und dauerhaft gespeichert.
  Autosync-Fehler lösen Wiederholungen nach 1, 5, 15 und danach jeweils 60 Minuten aus.
- Vorgangskennungen und gespeicherte Bestätigungen verhindern blind wiederholte
  Downloadübergaben nach Timeouts oder Neustarts. Unklare Übergaben werden zuerst abgeglichen.
- Ein Hintergrunddienst prüft aktive Downloads alle 30 Sekunden, unabhängig von
  geöffneten Benutzerseiten. Bibliotheksereignisse und ein Fünf-Minuten-Abgleich
  prüfen die tatsächliche Verfügbarkeit.
- Fertige Downloads geben Anfrageplätze frei. Dauerhafte Abos belegen keinen offenen Downloadplatz.
- Keine externen Benachrichtigungen über Discord, Telegram oder E-Mail.

## Projektstruktur

```text
.github/workflows/release.yml       Tests, Release-Pakete und Jellyfin-Repository
Jellyfin.Plugin.MediaForge/         Jellyfin-Plugin, Oberfläche und Anwendungslogik
MediaForge.Module/                  MediaForge-Modul und Installationshinweise
Tests/                             Python- und .NET-Sicherheits-/Workflowtests
scripts/                           Build, Versionspflege und Release-Prüfung
docs/WORKFLOW.md                    Migration, Wiederherstellung und API-Details
version.json                       Gemeinsame Versionsinformationen
```

## Diesen Ordner auf GitHub hochladen

1. Den **Inhalt dieses Ordners** in das Stammverzeichnis des Repositorys hochladen.
   `README.md`, `version.json`, `Jellyfin.Plugin.MediaForge` und die übrigen
   Projektordner müssen direkt auf der obersten Ebene liegen.
2. Den Ordner `.github` sowie `.gitignore` und `.gitattributes` mit übernehmen.
   Prüfen, dass `.github/workflows/release.yml` nach dem Upload vorhanden ist.
3. Bei einem bestehenden Repository die gleichnamigen Projektdateien aktualisieren.
   Keinen zusätzlichen Unterordner `Mediaforge-Jellyfin-Connector` im Repository anlegen.
4. Änderungen speichern beziehungsweise committen. Das reine Hochladen des
   Quellcodes veröffentlicht noch keine neue Plugin-Version; der enthaltene
   Release-Workflow startet erst bei einem gepushten Versionstag wie `v0.5.0`.

Der Upload-Ordner enthält Quellcode, Tests und Dokumentation. `.git`, lokale SDKs,
Caches, `bin`, `obj` und erzeugte Installationspakete sind nicht enthalten.
Installationspakete gehören in die GitHub-Release-Anhänge und werden durch den
Build beziehungsweise den Release-Workflow erzeugt.

## Voraussetzungen

- Jellyfin 10.11 oder neuer; Ziel-ABI dieser Version: `10.11.0.0`.
- MediaForge 1.5.x oder 1.6.x, vom Jellyfin-Server erreichbar.
- Gemeinsamer Zugriff auf die heruntergeladenen Mediendateien.
- Für eigene Builds: .NET 9 SDK und PowerShell; für Python-Tests zusätzlich Python 3.13.

Bei Docker müssen beide Container denselben Medienbestand sehen, beispielsweise:

```yaml
volumes:
  - /srv/media:/media
```

MediaForge kann dann nach `/media/Movies` und `/media/TV` schreiben, während Jellyfin
diese Verzeichnisse als Bibliotheken einliest. Die tatsächlichen Zielordner werden in MediaForge konfiguriert.

## Installation und Update

### 1. MediaForge-Modul installieren

Den Ordner `MediaForge.Module/mediaforge_jellyfin_connector` oder den gleichnamigen
Ordner aus dem Modul-ZIP nach folgendem Ziel kopieren:

```text
~/.mediaforge/thirdparties/mediaforge_jellyfin_connector/
```

Dort müssen `__init__.py`, `routes.py` und `operations.py` direkt liegen; den
Modulordner nicht doppelt verschachteln. MediaForge neu starten und unter
**Module Manager → Module Settings** prüfen, dass **Jellyfin Connector** aktiviert ist.

Unter **Settings → API** einen Schlüssel mit diesen Berechtigungen erstellen:

```text
status:read
library:read
queue:read
queue:write
```

Den Schlüssel direkt sichern; er wird nur einmal angezeigt. Weitere Hinweise:
[MediaForge-Modul](MediaForge.Module/README.md).

### 2. Jellyfin-Plugin installieren

Für eine manuelle Installation `MediaForgeRequests_0.5.0.zip` in einen eigenen
Pluginordner entpacken, unter Linux beispielsweise:

```text
/var/lib/jellyfin/plugins/MediaForgeRequests/
```

Der tatsächliche Pluginpfad hängt von der Jellyfin-Installation ab. Im Zielordner
müssen `Jellyfin.Plugin.MediaForge.dll` und `meta.json` direkt liegen. Jellyfin neu starten.

Alternativ kann nach Veröffentlichung einer Version das Jellyfin-Repository verwendet werden:

```text
Name: MediaForge Requests
URL:  https://daseric.github.io/Mediaforge-Jellyfin-Connector/manifest.json
```

Das Repository in Jellyfin unter **Dashboard → Plugins → Repositories** eintragen,
das Plugin aus dem Katalog installieren und Jellyfin neu starten. Welche Version
der Feed anbietet, hängt vom zuletzt erfolgreich veröffentlichten Release ab.

### 3. Verbindung und Benutzerzugriff konfigurieren

Unter **Dashboard → Plugins → My Plugins → MediaForge Requests → Settings**
die MediaForge-URL, den API-Schlüssel, Freigabemodus, erlaubte Quellen sowie
Standardsprache und Provider einstellen. **Test saved connection** prüft die
gespeicherte Verbindung; die Admin-Diagnose zeigt zusätzlich benötigte Fähigkeiten an.

Das Passwortfeld bleibt nach dem Speichern leer. Ein neuer Eintrag ersetzt den
gespeicherten Schlüssel. Der Schlüssel wird verschlüsselt gespeichert und nicht
an Benutzeroberflächen oder Jellix zurückgegeben.

**Show in the sidebar for all Jellyfin users** ist standardmäßig aktiviert.
Bereits geöffnete Jellyfin-Webseiten neu laden. Die Einbindung nutzt bevorzugt
das optionale File-Transformation-Plugin, sonst eine Anpassung von Jellyfins
`index.html`. Nach Jellyfin-Webupdates kann ein weiterer Serverneustart nötig sein.

### Migration und Sicherung

Vor dem Update die Jellyfin-Plugin-Daten und die MediaForge-Konfiguration sichern.
Die Migration auf Speicherschema 2 legt eine Sicherung `requests.json.v1-backup` an.
Alte Anfragen und Queue-IDs bleiben erhalten. Historische Freigaben erhalten
weder nachträgliche Autosync-Abos noch historische Mitteilungen. Alte noch offene
Anfragen verwenden bei einer neuen Freigabe den neuen Ablauf.

Die MediaForge-Datei `jellyfin-connector-receipts.sqlite3` muss ebenfalls gesichert
werden. Sie enthält die Bestätigungen der Downloadübergaben. Nicht löschen, um
eine Wiederholung zu erzwingen. In Jellyfin gehören `connector-secret.key` und
`mediaforge-api-key.bin` gemeinsam in die Sicherung.

## Optional: Jellix

Die bestehende Protokoll-v1-Integration mit `Jellix-for-Jellyfin` bleibt erhalten.
Jellix entdeckt nach Installation beider Jellyfin-Plugins und einem Neustart die
Bridge `Jellyfin.Plugin.MediaForge.Integration.JellixBridge`.

Suche, Anfrageerstellung und Status verwenden dieselbe Anwendungslogik wie
Jellyfin-Web, einschließlich der serverseitigen Benutzerregeln. Es sind weder
eine gemeinsame Vertrags-DLL noch ein zusätzlicher API-Schlüssel erforderlich.
Suchauswahlen verwenden kurzlebige, benutzergebundene Einmal-Tokens. Die
bestehenden Jellix-Protokollfelder bleiben kompatibel; neue Webfunktionen werden
nicht automatisch zu neuen Bedienelementen im Jellix-Client.

## Selbst bauen und testen

Im Stammverzeichnis ausführen:

```powershell
.\scripts\build.ps1
```

Falls `dotnet` nicht über `PATH` erreichbar ist:

```powershell
.\scripts\build.ps1 -DotNet C:\Pfad\zu\dotnet.exe
```

Ergebnis:

```text
dist/Jellyfin.Plugin.MediaForge.dll
dist/MediaForgeRequests_0.5.0.zip
dist/mediaforge_jellyfin_connector_0.5.0.zip
dist/SHA256SUMS.txt
```

Die Tests und Metadatenprüfung lassen sich separat ausführen:

```powershell
.\scripts\validate-release.ps1 -Tag v0.5.0
dotnet restore Tests/Connector.SecurityTests/Connector.SecurityTests.csproj --locked-mode
dotnet run --project Tests/Connector.SecurityTests/Connector.SecurityTests.csproj -c Release --no-restore
python -m pip install --require-hashes -r Tests/requirements-ci.txt
python -m unittest discover -s Tests -p "test_*.py"
ruff check MediaForge.Module Tests
node --check Jellyfin.Plugin.MediaForge/Web/requests.js
```

Generierte Builddateien können mit `scripts/clean.ps1` entfernt werden.

## Releases und automatische Pluginupdates

Der Workflow in `.github/workflows/release.yml` startet bei Tags mit dem Muster
`v*`. Er prüft Versionsangaben, führt Tests und Abhängigkeitsprüfungen aus,
erstellt beide Installations-ZIPs, veröffentlicht einen GitHub-Release und
überträgt `manifest.json` an GitHub Pages. Externe Actions sind auf Commit-SHAs
festgelegt; Python-Abhängigkeiten sind mit Hashes und NuGet-Abhängigkeiten mit
Lockdateien abgesichert.

GitHub Pages muss für den Actions-Workflow eingerichtet sein. Die
Deployment-Regeln der Umgebung `github-pages` müssen Versionstags `v*` zulassen.
Nach erfolgreicher Veröffentlichung liegt der Feed unter:

```text
https://DEIN-GITHUB-NAME.github.io/DEIN-REPOSITORY/manifest.json
```

Erst den geprüften Quellstand committen und pushen. Anschließend kann die Version
veröffentlicht werden, sofern der Tag noch nicht existiert:

```powershell
git tag -a v0.5.0 -m "MediaForge Requests 0.5.0"
git push origin v0.5.0
```

Bestehende veröffentlichte Tags nicht überschreiben. Für eine spätere Version
alle Versionsangaben mit `scripts/set-version.ps1` aktualisieren und den
Changelog prüfen, bevor der passende Tag erstellt wird.

Der Jellyfin-Updater aktualisiert nur das Jellyfin-Plugin. Das MediaForge-Modul
muss separat zuerst aktualisiert werden. Ein Pluginupdate erfordert normalerweise
einen Jellyfin-Neustart.

## Sicherheit und Betriebsgrenzen

- Admin-Endpunkte verwenden Jellyfins Adminrichtlinie; Benutzer dürfen nur eigene
  Beteiligungen und Mitteilungen verändern. Bibliothekslinks beachten Benutzerrechte.
- API-Schlüssel werden mit AES-256-GCM verschlüsselt. Unter Unix erhalten
  Schlüssel- und Geheimnisdateien zusätzlich Dateirechte `0600`.
- Quellen-URLs, Berechtigungen und Eingaben werden serverseitig geprüft. Adult-Quellen
  bleiben für API-Schlüssel durch MediaForges zentrale Altersprüfung gesperrt.
- API-Schlüssel, interne Dateipfade und ungefilterte MediaForge-Antworten werden
  nicht an Benutzer ausgeliefert. Poster werden über geprüfte Server-Proxys geladen.
- Für Verbindungen über Host- oder Netzwerkgrenzen HTTPS verwenden. HTTP nur für
  Loopback oder ein vertrauenswürdiges, nicht öffentliches Containernetz nutzen.
- Bei **Übergabe unklar** zuerst abgleichen. Eine ausdrücklich bestätigte erneute
  Übergabe kann einen doppelten Download erzeugen.
- Spätere Autosync-Downloads richten sich nach MediaForge. Dessen Autosync-API
  bietet keine eigene Upscaling-Option pro Abo; die Auswahl gilt für den Erstdownload.
- Das Plugin stellt keine Medien bereit. Nur Quellen und Inhalte verwenden,
  für deren Nutzung und Download die erforderlichen Rechte vorliegen.

## Prüfstatus der Version 0.5.0

Lokal erfolgreich geprüft: **40 Python-Tests**, die .NET-Sicherheits- und
Workflowtests, Release-Build ohne Warnungen, Ruff, JavaScript-Syntax sowie eine
Browserprüfung der Oberfläche mit simulierten API-Antworten.

Ein vollständiger Live-Test mit MediaForge 1.5 und 1.6, Jellyfin und Jellix steht
noch aus. Vor produktivem Einsatz insbesondere Providerauflösung, Zielordner,
Bibliothekszuordnung und das spätere Eintreffen neuer Folgen durch den echten
Autosync-Dienst prüfen. Das Vorhandensein dieses Quellstands bedeutet nicht,
dass Version 0.5.0 bereits veröffentlicht oder auf einem Server installiert wurde.

Weitere Details: [Workflow, Migration, Wiederherstellung und API](docs/WORKFLOW.md).

## Lizenz und Referenzen

Lizenz und Hinweise: [LICENSE](LICENSE), [NOTICE](NOTICE).

- [MediaForge](https://github.com/PD-Codes/MediaForge)
- [Jellyfin-AniWorld-Downloader](https://github.com/SiroxCW/Jellyfin-AniWorld-Downloader)
