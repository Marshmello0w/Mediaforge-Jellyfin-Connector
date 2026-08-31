# MediaForge Jellyfin Connector

Filme und Serien direkt in Jellyfin suchen und anfragen. Administratoren können
Anfragen freigeben oder automatisch freigeben lassen. MediaForge übernimmt die
Downloads; neu freigegebene Serien werden zusätzlich dauerhaft in Autosync aufgenommen.

**Version: 0.5.5 · Jellyfin ab 10.11 · MediaForge 1.5.x / 1.6.x**

## Neu in 0.5.5: Menüzähler und neueste Anfragen zuerst

Am Drei-Striche-Menü und direkt neben „Anfragen“ in Jellyfin-Web erscheint derselbe kleine rote Kreis mit
weißer Zahl, sobald Anfragen auf Freigabe warten. Kein Popup und kein zusätzlicher
Textkasten. Bei null offenen Freigaben verschwindet der Kreis; normale Benutzer
sehen ihn nicht. Das Menü bleibt normal bedienbar.

Die Anzahl wird bei sichtbarem Browserfenster alle 30 Sekunden aktualisiert,
zusätzlich nach Freigabe/Ablehnung im Plugin und beim Zurückkehren zur Seite.
Vollständig geteilte Anfragen werden in der Freigabeliste und im Zähler einmal
gezählt. Ab 100 zeigt der Kreis `99+`; die genaue Zahl bleibt für Screenreader verfügbar.
Beim Abmelden, Benutzerwechsel oder fehlender Berechtigung wird der Zähler entfernt.

Eigene Anfragen und die Adminliste sind nach Anfragedatum von neu nach alt sortiert,
unabhängig vom Status. Neue Einträge erscheinen auch bei laufender Aktualisierung
oben; unveränderte Karten, Auswahl und aufgeklappte Verläufe bleiben erhalten.
Adminfilter und Seitennavigation funktionieren weiterhin.

Für diese Funktion genügt das Jellyfin-Plugin **0.5.5.0**: Jellyfin nach dem Update
neu starten und Jellyfin-Web neu laden (gegebenenfalls den Browser-Cache leeren).
Die bestehende Web-Einbindung des Plugins muss aktiviert sein (`EnableAllUsers`,
standardmäßig aktiv). Native Clients ohne Jellyfin-Web erhalten diesen Menüpunkt
nicht automatisch. Die MediaForge-Modulfunktionen sind gegenüber 0.5.3 unverändert.

## Autosync-Korrektur und Diagnose in 0.5.3

Der Connector verwendet jetzt die dokumentierte `mediaforge_raw_views`-Schnittstelle
für interne MediaForge-Aufrufe, auch wenn Autosync erst nach dem Modul registriert
wird. Unter älteren Builds bleibt der eingeschränkte Kompatibilitätsweg erhalten.
API-Key-Prüfung, `queue:write`, Quellenfreigaben und Serienprüfung bleiben erforderlich.

Die Admin-Diagnose zeigt zusätzlich, ob die Autosync-Funktion geladen ist und der
Core-Aufruf über die Modul-API läuft. Autosync-Fehler werden mit einer sicheren
Ursachenbeschreibung und HTTP-Status angezeigt; interne Fehlermeldungen, Schlüssel
oder Dateipfade werden nicht weitergegeben. Ein Retry verändert keine Download-Queue.

Getestet mit den realen Autosync-, Authentifizierungs- und SQLite-Funktionen aus
MediaForge **v1.6.0**, zusätzlich zu den automatisierten Connector-Regressionstests.
Das ersetzt keine Prüfung der jeweiligen laufenden Serverkonfiguration.

Referenz: [MediaForge Module API – mediaforge_raw_views](https://github.com/PD-Codes/MediaForge/wiki/Module-API#reaching-a-core-view-without-a-session-mediaforge_raw_views).

## Sprachkorrekturen seit 0.5.2

- German Dub, German Sub und English Sub bleiben getrennt auswählbar, auch wenn
  die Synchronisation noch nicht für alle Staffeln und Folgen vorliegt.
- Der Dialog zeigt die Anzahl fehlender Folgen pro Sprache. Angefragt werden nur
  Folgen, die laut MediaForge in dieser Sprache verfügbar sind; die Freigabe prüft
  dieselbe Sprachauswahl erneut. Nicht verfügbare Folgen gelten nicht als vorhanden.
- Die Hoster-Auswahl verarbeitet MediaForges verschachtelte `providers`-Antwort
  sowie das ältere flache Format und prüft passende Beispielfolgen je Sprache.
- Autosync akzeptiert die Serienantworten von MediaForge 1.5/1.6 ohne `is_movie`-Feld.
  Filme, fehlerhafte Antworten, gesperrte Quellen und unberechtigte Aufrufe bleiben gesperrt.

**Update:** Zuerst das MediaForge-Modul auf **0.5.3** aktualisieren und MediaForge
neu starten; danach das Jellyfin-Plugin auf **0.5.3.0** aktualisieren und Jellyfin
neu starten. Bereits ausstehende Autosync-Aufträge werden automatisch erneut versucht.
Admins können alternativ **Nur Autosync erneut versuchen** wählen. Dafür keine
neue Downloadanfrage anlegen. Bestehende Autosync-Pausen und Zielordner bleiben erhalten.

## Wechsel auf die Marshmello-Variante (seit 0.5.1)

Diese Variante hat die eigene Modul-ID und den eigenen Installationsordner
`marshmello_jellyfin_connector`. Im Store heißt sie **Jellyfin Connector – Marshmello**.
Damit wird sie unabhängig vom offiziellen `mediaforge_jellyfin_connector` (0.4.3)
angeboten. Eine höhere Versionsnummer allein löst die Kollision gleicher IDs nicht,
da MediaForge den offiziellen Store bei gleichen IDs bevorzugt.

1. Den aktualisierten Quellstand einschließlich `module-store` auf GitHub hochladen.
2. In MediaForge den zusätzlichen Store aktualisieren und **Jellyfin Connector – Marshmello**
   neu installieren. Die neue ID ist eine separate Installation, kein Update der offiziellen Karte.
3. MediaForge neu starten. Die Marshmello-Karte muss Version **0.5.3** anzeigen.
4. **Auch das Jellyfin-Plugin auf 0.5.3 aktualisieren und Jellyfin neu starten.**
   Es nutzt jetzt `/api/v1/marshmello-connector/`. Die Versionen 0.4.x/0.5.0 des
   Jellyfin-Plugins nutzen weiter die alten API-Adressen und wechseln nicht automatisch.
5. Gespeicherte Verbindung und Admin-Diagnose prüfen. Die MediaForge-Basis-URL
   und die benötigten API-Berechtigungen bleiben unverändert.

Das offizielle Modul darf parallel installiert bleiben: Ordner, Modul-ID,
Flask-Blueprint, Einstellungen und API-Adressen sind getrennt. Seine Updates
adressieren nicht die Marshmello-Installation. Auf automatische Downloads über
andere Clients hat der Wechsel keinen Einfluss. Beide Varianten verwenden
weiterhin dieselbe MediaForge-Queue und dieselben Autosync-Abos.

Anfragen und Einstellungen des Jellyfin-Plugins bleiben beim Update erhalten.
Die vorhandene `jellyfin-connector-receipts.sqlite3` wird für die Wiederherstellung
früherer Übergaben weiterverwendet und darf nicht gelöscht werden.

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
module-store/                      Modulkatalog und installierbares .mfmod-Paket
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
   Release-Workflow startet erst bei einem gepushten Versionstag wie `v0.5.3`.

Der Upload-Ordner enthält Quellcode, Tests und Dokumentation. `.git`, lokale SDKs,
Caches, `bin` und `obj` sind nicht enthalten. Die ZIP-Installationspakete werden
durch den Build beziehungsweise Release-Workflow erzeugt. Eine bewusste Ausnahme
ist das kleine `.mfmod`-Paket im Ordner `module-store`: Es wird mit hochgeladen,
damit MediaForge es direkt über den Modulkatalog installieren kann.

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

**Über den Modulmanager (empfohlen, wenn der Store unterstützt wird):**
Den enthaltenen Ordner `module-store` zusammen mit dem Projekt in dein
öffentliches Repository hochladen. Unter **Weitere Repositories** diese URL
eintragen:

```text
https://raw.githubusercontent.com/Marshmello0w/Mediaforge-Jellyfin-Connector/main/module-store/index.json
```

Speichern, den Store aktualisieren und **Jellyfin Connector – Marshmello** installieren oder
aktualisieren. Da das Paket keine Signatur der MediaForge-Maintainer besitzt,
muss die Installation unverifizierter Module ausdrücklich erlaubt werden –
nur aktivieren, wenn du dem Quellcode vertraust. Danach MediaForge neu starten.
Der Link funktioniert erst, wenn der Ordner tatsächlich hochgeladen wurde.
GitHub Pages und ein Release sind für diesen Raw-Link nicht erforderlich.

Die Marshmello-Variante wird unter einer eigenen Modul-ID installiert. Spätere
Updates vergleichen nur Versionen dieser ID. Versionsnummern des Moduls und des
Katalogs müssen zusammenpassen. Details: [Modulkatalog](module-store/README.md).

**Alternativ manuell, auch ohne Store:**

Den Ordner `MediaForge.Module/marshmello_jellyfin_connector` oder den gleichnamigen
Ordner aus dem Modul-ZIP nach folgendem Ziel kopieren:

```text
~/.mediaforge/thirdparties/marshmello_jellyfin_connector/
```

Dort müssen `__init__.py`, `routes.py` und `operations.py` direkt liegen; den
Modulordner nicht doppelt verschachteln. MediaForge neu starten und unter
**Module Manager → Module Settings** prüfen, dass **Jellyfin Connector – Marshmello** aktiviert ist.

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

Für eine manuelle Installation `MediaForgeRequests_0.5.3.zip` in einen eigenen
Pluginordner entpacken, unter Linux beispielsweise:

```text
/var/lib/jellyfin/plugins/MediaForgeRequests/
```

Der tatsächliche Pluginpfad hängt von der Jellyfin-Installation ab. Im Zielordner
müssen `Jellyfin.Plugin.MediaForge.dll` und `meta.json` direkt liegen. Jellyfin neu starten.

Alternativ kann nach Veröffentlichung einer Version das Jellyfin-Repository verwendet werden:

```text
Name: MediaForge Requests
URL:  https://marshmello0w.github.io/Mediaforge-Jellyfin-Connector/manifest.json
```

Das Repository in Jellyfin unter **Dashboard → Plugins → Repositories** eintragen,
das Plugin aus dem Katalog installieren und Jellyfin neu starten. Welche Version
der Feed anbietet, hängt vom zuletzt erfolgreich veröffentlichten Release ab.

Dies ist der
**Jellyfin-Feed** und nicht der MediaForge-Modulkatalog. Der Feed existiert
erst nach einem erfolgreichen Release mit GitHub-Pages-Deployment.

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
dist/MediaForgeRequests_0.5.3.zip
dist/marshmello_jellyfin_connector_0.5.3.zip
dist/SHA256SUMS.txt
```

Zusätzlich wird `module-store` mit `index.json`, `index-all.json` und dem
`.mfmod`-Paket aktualisiert. Alle Dateien dieses Ordners gemeinsam hochladen.
Nur den Modulkatalog ohne .NET-Build neu erzeugen:

```powershell
.\scripts\generate-module-store.ps1 -RepositorySlug Marshmello0w/Mediaforge-Jellyfin-Connector
```

Ohne `-RepositorySlug` wird lokal `Marshmello0w/Mediaforge-Jellyfin-Connector`
verwendet. Im Release-Workflow wird automatisch das tatsächliche GitHub-Repository
verwendet; bei einem weiteren Fork kann der Parameter die Adresse überschreiben.

Die Tests und Metadatenprüfung lassen sich separat ausführen:

```powershell
.\scripts\validate-release.ps1 -Tag v0.5.3
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
https://marshmello0w.github.io/Mediaforge-Jellyfin-Connector/manifest.json
```

Erst den geprüften Quellstand committen und pushen. Anschließend kann die Version
veröffentlicht werden, sofern der Tag noch nicht existiert:

```powershell
git tag -a v0.5.3 -m "MediaForge Requests 0.5.3"
git push origin v0.5.3
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

## Prüfstatus der Version 0.5.3

Lokal erfolgreich geprüft: **42 Python-Tests**, einschließlich getrennter
API-Routen bei gleichzeitig registriertem offiziellen Connector, die .NET-Sicherheits- und
Workflowtests, Release-Build ohne Warnungen, Ruff, JavaScript-Syntax sowie eine
Browserprüfung der Oberfläche mit simulierten API-Antworten.

Ein vollständiger Live-Test mit MediaForge 1.5 und 1.6, Jellyfin und Jellix steht
noch aus. Vor produktivem Einsatz insbesondere Providerauflösung, Zielordner,
Bibliothekszuordnung und das spätere Eintreffen neuer Folgen durch den echten
Autosync-Dienst prüfen. Das Vorhandensein dieses Quellstands bedeutet nicht,
dass Version 0.5.3 bereits veröffentlicht oder auf einem Server installiert wurde.

Weitere Details: [Workflow, Migration, Wiederherstellung und API](docs/WORKFLOW.md).

## Lizenz und Referenzen

Lizenz und Hinweise: [LICENSE](LICENSE), [NOTICE](NOTICE).

- [MediaForge](https://github.com/PD-Codes/MediaForge)
- [Jellyfin-AniWorld-Downloader](https://github.com/SiroxCW/Jellyfin-AniWorld-Downloader)
