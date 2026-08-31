# Änderungsverlauf

[Zurück zur README](README.md)

Änderungen der Marshmello-Variante, von neu nach alt. Die Versionsnummern gelten
für das Projekt; das Jellyfin-Plugin verwendet zusätzlich eine vierte Stelle,
zum Beispiel `0.5.6.0`. Die Historie dieser Variante beginnt mit 0.5.0.

## 0.5.6 – Eigener Admin-Tab für Benutzerregeln

- Adminbereich in die Untertabs **Anfragen** und **Benutzerregeln** aufgeteilt.
- Freigabemodus, Anfragelimit und Serien-Abo-Berechtigung bleiben wie gewohnt bearbeitbar.
- Neue Liste **Direkte Freigabe aktiviert** für Benutzer mit individuell eingestellter
  automatischer Freigabe. Benutzer, die den globalen Modus erben, werden nicht aufgeführt.
- Das **× rechts neben dem Namen** setzt den Freigabemodus auf **Globale Einstellung**
  zurück. Limit und Abo-Berechtigung bleiben erhalten. Ist die globale Freigabe
  automatisch, gilt das auch nach dem Zurücksetzen.
- Zurücksetzen ist durch Adminrechte geschützt, atomar gespeichert und protokolliert.
  Wiederholte Aufrufe überschreiben keine inzwischen gesetzte manuelle Regel.
- Speichern und Zurücksetzen aktualisieren die Liste unmittelbar. Bei Fehlern bleibt
  der Eintrag erhalten; während der Verarbeitung sind die Regelschaltflächen gesperrt.
- .NET-Tests um Zurücksetzen, Neustart, parallele Aufrufe und globale Vererbung ergänzt;
  Oberfläche mit simulierten API-Antworten geprüft.

**Update:** Jellyfin-Plugin auf `0.5.6.0` aktualisieren, Jellyfin neu starten und
Jellyfin-Web neu laden. Für diese Änderungen ist kein MediaForge-Modulupdate nötig.

## 0.5.5 – Zähler neben „Anfragen“ und neueste Anfragen zuerst

- Derselbe rote Freigabezähler erscheint zusätzlich neben **Anfragen** im Seitenmenü.
  Beide Anzeigen verwenden dieselbe Abfrage.
- Eigene Anfragen, die ältere Listen-API und die paginierte Adminübersicht sortieren
  nach Anfragedatum absteigend. Bei gleichem Datum entscheidet die höhere ID.
  Fehler und offene Anfragen werden nicht mehr unabhängig vom Datum vorgezogen.
- Neue Anfragen erscheinen auch bei laufender Aktualisierung oben. Unveränderte
  Karten, Auswahl und aufgeklappte Verläufe bleiben erhalten.
- Filter und Seitennavigation bleiben nutzbar; Tests für Sortierung und Seitengrenzen ergänzt.

**Update:** Jellyfin-Plugin `0.5.5.0`, anschließend Jellyfin-Neustart und Neuladen
der Webseite. Die MediaForge-Modulfunktionen sind unverändert.

## 0.5.4 – Freigabezähler für Admins

- Kleiner roter Kreis mit weißer Zahl am Drei-Striche-Menü von Jellyfin-Web,
  sobald Anfragen auf Freigabe warten. Kein Popup oder zusätzlicher Textkasten.
- Anzeige nur für Admins; bei null offenen Freigaben, Abmeldung, fehlenden Rechten
  oder Abfragefehlern verschwindet sie. Das Menü bleibt normal bedienbar.
- Aktualisierung alle 30 Sekunden bei sichtbarem Browserfenster sowie nach
  Freigabe/Ablehnung und beim Zurückkehren zur Seite.
- Vollständig geteilte offene Anfragen werden einmal gezählt. Ab 100 erscheint
  `99+`; die genaue Zahl bleibt für Screenreader verfügbar.
- Admin-geschützter Zähler-Endpunkt liefert keine Titel oder Benutzeridentitäten.
  Verspätete Antworten nach Benutzer- oder Serverwechsel werden verworfen.

**Update:** Jellyfin-Plugin `0.5.4.0`, anschließend Neustart und Neuladen der
Webseite; gegebenenfalls Browser-Cache leeren. Die Web-Einbindung muss aktiviert
sein (`EnableAllUsers`, standardmäßig aktiv). Native Clients ohne Jellyfin-Web
erhalten diese Anzeige nicht automatisch. Die Modulfunktionen sind gegenüber 0.5.3 unverändert.

## 0.5.3 – Autosync-Korrektur und Diagnose

- Interne MediaForge-Aufrufe verwenden die dokumentierte
  `app.extensions["mediaforge_raw_views"]`-Schnittstelle, auch bei später registrierter
  Autosync-Funktion. Für ältere Builds bleibt ein eingeschränkter Kompatibilitätsweg.
- API-Key-Prüfung, `queue:write`, Quellenfreigaben und Serienprüfung bleiben erforderlich.
- Admin-Diagnose zeigt, ob die Autosync-Funktion geladen ist und der Core-Aufruf
  über die Modul-API erfolgt.
- Autosync-Fehler zeigen eine sichere Ursachenbeschreibung und den HTTP-Status;
  interne Fehlermeldungen, Schlüssel und Dateipfade werden nicht weitergegeben.
- Autosync-Wiederholungen verändern keine Download-Queue.
- Mit den realen Autosync-, Authentifizierungs- und SQLite-Funktionen aus MediaForge
  1.6.0 sowie automatisierten Connector-Regressionstests geprüft. Das ersetzt
  keinen vollständigen Test der jeweiligen laufenden Serverkonfiguration.

**Update:** Zuerst MediaForge-Modul `0.5.3` installieren und MediaForge neu starten,
danach Jellyfin-Plugin `0.5.3.0` und Jellyfin neu starten. Ausstehende Autosync-Aufträge
werden erneut versucht. Alternativ **Nur Autosync erneut versuchen** wählen;
keine neue Downloadanfrage anlegen. Bestehende Pausen und Zielordner bleiben erhalten.

Referenz: [MediaForge Module API – mediaforge_raw_views](https://github.com/PD-Codes/MediaForge/wiki/Module-API#reaching-a-core-view-without-a-session-mediaforge_raw_views).

## 0.5.2 – Dub-/Sub-Erkennung und Serienanmeldung

- German Dub, German Sub und English Sub bleiben getrennt auswählbar, auch wenn
  eine Sprache noch nicht für alle Staffeln und Folgen verfügbar ist.
- Dialog zeigt die Anzahl fehlender Folgen pro Sprache. Angefragt werden nur Folgen,
  die MediaForge in der gewählten Sprache anbietet; die Freigabe prüft dieselbe Auswahl erneut.
  Nicht verfügbare Folgen gelten nicht als bereits vorhanden.
- Hoster-Auswahl unterstützt verschachtelte `providers`-Antworten sowie das ältere
  flache Format und prüft passende Beispielfolgen je Sprache.
- Autosync akzeptiert Serienantworten von MediaForge 1.5/1.6 ohne `is_movie`-Feld.
  Filme, fehlerhafte Antworten, gesperrte Quellen und unberechtigte Aufrufe bleiben gesperrt.

## 0.5.1 – Eigenständige Marshmello-Modulidentität

- Eigene Modul-ID und eigener Installationsordner `marshmello_jellyfin_connector`;
  Store-Name **Jellyfin Connector – Marshmello**.
- Eigener Flask-Blueprint, eigene Einstellungen und API-Adressen unter
  `/api/v1/marshmello-connector/`, damit Updates des offiziellen Moduls die Variante
  nicht überschreiben. Eine höhere Versionsnummer allein verhindert die ID-Kollision nicht.
- Offizielles Modul und Marshmello-Modul können parallel installiert sein und
  verwenden weiterhin dieselbe MediaForge-Queue und dieselben Autosync-Abos.
- Bestehende Pluginanfragen, Einstellungen, Abos und dauerhafte Übergabebestätigungen
  bleiben erhalten. Die `jellyfin-connector-receipts.sqlite3` wird weiterverwendet.
- Koexistenz der API-Routen auf modernen und älteren MediaForge-Schnittstellen geprüft.
- Release-Pakete korrigiert und manuelle Veröffentlichung über GitHub Actions ermöglicht.

**Umstieg:** Die Marshmello-Karte separat installieren und auch das Jellyfin-Plugin
aktualisieren. Pluginversionen 0.4.x/0.5.0 verwenden noch die alten API-Adressen.
Aktuelle Anleitung: [Installation und Update](README.md#installation-und-update).

## 0.5.0 – Autosync und erweiterter Anfrageablauf

- Neu manuell oder automatisch freigegebene Serien dauerhaft in Autosync aufnehmen,
  ohne bestehende Abos zu verändern; Filme erhalten kein Abo.
- Dauerhaft gespeicherte Downloadbestätigungen und vorsichtige Wiederherstellung
  nach unterbrochenen Übergaben. Download und Autosync werden getrennt verarbeitet.
- Hintergrundabgleich von Downloadstatus und Jellyfin-Verfügbarkeit.
- Gemeinsame Anfragen, Benutzerregeln und Abonnieren vollständiger Serien ergänzt.
- Adminübersicht mit Seitennavigation, Mehrfachentscheidungen, getrennten
  Wiederherstellungsaktionen und Diagnose.
- Dauerhaft gespeicherte Mitteilungen innerhalb des Plugins und tägliche
  Zusammenfassungen neuer Folgen.

Frühere Versionen stammen aus dem [ursprünglichen Projekt](https://github.com/DasEric/Mediaforge-Jellyfin-Connector).
