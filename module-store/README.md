# MediaForge-Modulkatalog

Dieser Ordner ist ein direkt auslieferbarer Modulkatalog für den MediaForge-Modulmanager.
`index.json` und `index-all.json` verweisen auf ein `.mfmod`-Paket mit SHA-256-Prüfsumme.
Das Paket enthält genau den Modulordner mit seinen Python-Dateien.

Nach dem Upload aller Dateien nach `main` im öffentlichen GitHub-Repository
unter **Weitere Repositories** diese URL eintragen:

```text
https://raw.githubusercontent.com/DEIN-GITHUB-NAME/DEIN-REPOSITORY/main/module-store/index.json
```

Speichern, unverifizierte Module nur bei Vertrauen in diesen Quellcode zulassen,
den Store aktualisieren und **Jellyfin Connector** installieren beziehungsweise
aktualisieren. Anschließend MediaForge neu starten. Die Signaturprüfung bleibt
unverändert: Dieses Paket ist nicht durch MediaForge-Maintainer signiert.

Der GitHub-Upload allein macht diesen Katalog nutzbar; ein GitHub-Release und
GitHub Pages sind für den Raw-Link nicht nötig. Das Jellyfin-Plugin wird separat
installiert. Nach erfolgreichem Release/Pages-Deployment ist der Katalog außerdem
unter folgendem Link erreichbar:

```text
https://DEIN-GITHUB-NAME.github.io/DEIN-REPOSITORY/module-store/index.json
```

## Aktualisierung

Modulversion und Katalogversion müssen übereinstimmen und für ein Update höher
als die bereits installierte Version sein. 0.5.0 ersetzt 0.4.3. Bei bereits
installierter Version 0.5.0 ist kein neueres Update vorhanden.

Nach Änderungen an den Moduldateien neu erzeugen:

```powershell
.\scripts\generate-module-store.ps1 -RepositorySlug DEIN-GITHUB-NAME/DEIN-REPOSITORY
```

Bei einer neuen Veröffentlichung zuerst `scripts/set-version.ps1` verwenden;
anschließend den Katalog neu erzeugen. `scripts/build.ps1` erledigt die
Katalogerzeugung ebenfalls. Indexdateien und Paket immer zusammen hochladen,
damit Prüfsumme und Version stimmen. Erzeugte Dateien dieses Ordners werden
absichtlich im Repository gespeichert, damit der direkte Upload funktioniert.

Eine Versionsnummer macht ein Modul weder signiert noch mit einer neueren
MediaForge-Hauptversion kompatibel. Die deklarierte Grenze bleibt 1.5.x–1.6.x.
