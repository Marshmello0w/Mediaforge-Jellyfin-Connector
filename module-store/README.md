# MediaForge-Modulkatalog

Dieser Ordner ist ein direkt auslieferbarer Modulkatalog für den MediaForge-Modulmanager.
`index.json` und `index-all.json` verweisen auf ein `.mfmod`-Paket mit SHA-256-Prüfsumme.
Das Paket enthält genau den Modulordner mit seinen Python-Dateien.

Nach dem Upload aller Dateien nach `main` im öffentlichen GitHub-Repository
unter **Weitere Repositories** diese URL eintragen:

```text
https://raw.githubusercontent.com/Marshmello0w/Mediaforge-Jellyfin-Connector/main/module-store/index.json
```

Speichern, unverifizierte Module nur bei Vertrauen in diesen Quellcode zulassen,
den Store aktualisieren und **Jellyfin Connector – Marshmello** installieren beziehungsweise
aktualisieren. Anschließend MediaForge neu starten. Die Signaturprüfung bleibt
unverändert: Dieses Paket ist nicht durch MediaForge-Maintainer signiert.

Der GitHub-Upload allein macht diesen Katalog nutzbar; ein GitHub-Release und
GitHub Pages sind für den Raw-Link nicht nötig. Das Jellyfin-Plugin wird separat
installiert. Nach erfolgreichem Release/Pages-Deployment ist der Katalog außerdem
unter folgendem Link erreichbar:

```text
https://marshmello0w.github.io/Mediaforge-Jellyfin-Connector/module-store/index.json
```

## Aktualisierung

Die Modul-ID `marshmello_jellyfin_connector` unterscheidet sich absichtlich von
der offiziellen ID `mediaforge_jellyfin_connector`. **Jellyfin Connector – Marshmello**
wird daher beim Wechsel neu installiert; es ersetzt die offizielle Karte nicht.
Nach der Modulinstallation MediaForge neu starten und das Jellyfin-Plugin auf
0.5.1 aktualisieren, damit es die getrennte API verwendet. Beide Module dürfen
parallel installiert bleiben. Der zusätzliche Repository-Link bleibt gleich.

Für spätere Updates müssen Modulversion und Katalogversion übereinstimmen und
höher als die bereits installierte Marshmello-Version sein.

Nach Änderungen an den Moduldateien neu erzeugen:

```powershell
.\scripts\generate-module-store.ps1 -RepositorySlug Marshmello0w/Mediaforge-Jellyfin-Connector
```

Bei einer neuen Veröffentlichung zuerst `scripts/set-version.ps1` verwenden;
anschließend den Katalog neu erzeugen. `scripts/build.ps1` erledigt die
Katalogerzeugung ebenfalls. Indexdateien und Paket immer zusammen hochladen,
damit Prüfsumme und Version stimmen. Erzeugte Dateien dieses Ordners werden
absichtlich im Repository gespeichert, damit der direkte Upload funktioniert.

Eine Versionsnummer macht ein Modul weder signiert noch mit einer neueren
MediaForge-Hauptversion kompatibel. Die deklarierte Grenze bleibt 1.5.x–1.6.x.
