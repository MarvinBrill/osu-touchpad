# osu! touchpad

Ein Android-Handy als absolutes Grafiktablett für osu! auf Windows 11. Der erste Finger zielt auf dem PC-Monitor. Zum Tippen dienen die PC-Tastatur oder ein zweiter Finger. Alle Einstellungen liegen in der Windows-Tray-App.

## Bauen und starten

**Windows.** Öffne die [Visual-Studio-Lösung](windows/OsuTouchpad.sln) mit Visual Studio 2026 und der Workload **.NET-Desktopentwicklung**. Wähle `OsuTouchpad` als Startprojekt. Alternativ im Repo-Stamm mit dem .NET-10-SDK in PowerShell ausführen.

```powershell
dotnet build .\windows\OsuTouchpad.sln -c Release
& '.\windows\OsuTouchpad\bin\Release\net10.0-windows\OsuTouchpad.exe'
```

Das Fenster öffnet sich beim Start. Schließen legt es in den Infobereich. Ein Doppelklick auf das Tablettsymbol öffnet die Einstellungen erneut. Über **Beenden** im Kontextmenü schließt du den Empfänger. Zum Übertragen auf einen anderen Windows-PC den gesamten Ausgabeordner mitnehmen. Er benötigt die .NET-10-Desktop-Runtime.

**Android.** Öffne den Ordner `android/` in Android Studio. Das Java-Projekt verwendet Android SDK Platform 34, Build Tools 36.0.0 und den mitgelieferten Gradle-Wrapper. Wähle das freigegebene Galaxy S23 und starte **Run 'app'**. Alternativ mit den Standardpfaden von Android Studio im Repo-Stamm bauen und installieren. Die App unterstützt Android ab 8.0 (API 26). [Kompatibilität des Android-Gradle-Plugins](https://developer.android.com/build/releases/agp-9-1-0-release-notes)

```powershell
$env:JAVA_HOME = Join-Path $env:ProgramFiles 'Android\Android Studio\jbr'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
.\android\gradlew.bat -p android :app:assembleDebug
& "$env:ANDROID_HOME\platform-tools\adb.exe" -d install -r .\android\app\build\outputs\apk\debug\app-debug.apk
```

Die installierbare Debug-APK liegt unter `android/app/build/outputs/apk/debug/app-debug.apk`. Öffne auf dem Handy **osu! touchpad**.

## Einmalige Einrichtung

### USB

1. Auf dem S23 die Entwickleroptionen und **USB-Debugging** aktivieren. Das entsperrte Handy mit einem USB-Datenkabel anschließen und den RSA-Dialog für diesen PC bestätigen. [ADB und Gerätefreigabe](https://developer.android.com/tools/adb)
2. In der Windows-App **USB** auswählen. Der Empfänger sucht ADB im Android-SDK und im `PATH`, erkennt das autorisierte Gerät und richtet `adb -s <Seriennummer> reverse tcp:27183 tcp:27183` ein.
3. Die Handy-App verbindet sich über `127.0.0.1:27183`. Windows zeigt Gerätenamen und **Verbunden**. Auf dem Handy verschwinden die Statusmeldungen. Bei späteren Sitzungen genügen die geöffneten Apps und das Einstecken des Kabels.

Bleibt Windows bei der USB-Suche, im SDK Manager **Android SDK Platform-Tools** prüfen. Bei einem benutzerdefinierten SDK-Pfad `ANDROID_HOME` setzen oder `platform-tools` zum `PATH` hinzufügen und den Empfänger neu starten. Der Befehl `adb devices -l` zeigt das S23 nach erfolgreicher Freigabe als `device`.

### WLAN

1. Am PC **WLAN** auswählen und der Windows-Firewall den Zugriff im **privaten Netzwerk** erlauben. Handy und PC brauchen dasselbe lokale IPv4-Broadcastnetz. Der PC kann per Ethernet am Router hängen.
2. Beim ersten Verbinden die sechs Ziffern auf Handy und PC vergleichen. Stimmen sie überein, am PC **Koppeln** wählen. Der bekannte PC wird beim nächsten Start auch nach einer Änderung seiner IP-Adresse automatisch gefunden.
3. Ohne Kabel sucht die Handy-App direkt im WLAN. Mit Kabel versucht sie zuerst ADB und sucht bei ausbleibender Antwort im WLAN weiter. Eine bestehende WLAN-Sitzung läuft beim Einstecken eines Ladekabels weiter.

| Zweck | Port am PC | Übertragung |
| --- | --- | --- |
| USB | TCP 27183, nur Loopback | ADB Reverse, Positionen und Tasten in einem geordneten Datenstrom |
| WLAN-Suche | UDP 27185 | IPv4-Broadcast mit Unicast-Antwort des PCs |
| WLAN-Sitzung | TCP 27184 | TLS, Kopplung, Konfiguration, Beginn und Ende des Zielens, Tastenwechsel und Heartbeat |
| WLAN-Bewegung | UDP 27184 | Absolute Position, Konfigurationsstand, Gestenphase, Sequenznummer und sitzungsgebundener HMAC |

Bei ausbleibender WLAN-Erkennung die eingehenden Ports im privaten Firewallprofil und die Client-Isolation des Routers prüfen. Gastnetze trennen häufig WLAN-Geräte und Ethernet. Der Empfänger nimmt zu jedem Zeitpunkt eine Eingabesitzung über den gewählten Transport an.

## Touchfläche und Ziel einstellen

- Unter **Ziel & Eingabe** den Spielmonitor wählen. Für einen Ausschnitt **Gesamten Monitor verwenden** abwählen. Links und oben zählen in physischen Pixeln ab der linken oberen Ecke dieses Monitors. Windows berücksichtigt dessen Position und Skalierung im virtuellen Desktop.
- Unter **Touchfläche** den roten Rahmen innen ziehen, an einer Kante strecken oder an einer Ecke in zwei Richtungen skalieren. Die vier Prozentfelder haben eine Auflösung von 0,01 %. Der Rahmen auf dem Handy und die Eingabe verwenden dieselben Koordinaten. Eine kleinere Fläche verkürzt den Fingerweg.
- **1:1** erzeugt ein physisch quadratisches Feld auf dem Handy. Die anderen Formatknöpfe setzen gebräuchliche Verhältnisse. Breite und Höhe sind frei editierbar. **An Seitenverhältnis des Zielbereichs binden** hält beim Skalieren das tatsächliche Format des PC-Ziels. Die Vorschau übernimmt die Maße der verbundenen Android-Ansicht.
- Unter **Taste für den zweiten Finger** stehen Links, Rechts, Mitte, Maustaste 4/5 und **Deaktiviert · PC-Tastatur** bereit. Die Voreinstellung ist die linke Maustaste. Ein Kontakt, der außerhalb des Rahmens beginnt, bleibt bis zum Abheben wirkungslos.

Beim Ändern der Zuordnung endet eine laufende Geste. Hebe die Finger kurz ab und setze im neuen Rahmen wieder auf. Windows speichert die Einstellungen unter `%LOCALAPPDATA%\osu-touchpad\settings.json` und überträgt sie bei jeder Verbindung. Im selben Ordner liegen die lokale WLAN-Identität und die gekoppelten Geräte. Auf Android bleiben die Kopplungsdaten im privaten App-Speicher.

## Manueller Abnahmetest

| Schritt | Erwartetes Ergebnis |
| --- | --- |
| Auf dem 3440×1440-Monitor die vier Ecken und die Mitte der roten Fläche antippen. Auf einem zweiten Monitor und in einem versetzten Zielrechteck wiederholen, auch mit unterschiedlicher Windows-Skalierung. | Der Cursor trifft den entsprechenden Zielpunkt schon beim Aufsetzen. Beim Abheben bleibt er stehen. |
| Rahmen verschieben, an Kante und Ecke skalieren, ein Prozentfeld ändern und **1:1** wählen. Anschließend die Formatsperre einschalten und eine Kante ziehen. | Der Handyrahmen folgt sofort. Die neue Grenze wirkt auch für Eingaben. Das Quadrat ist auf dem Handy gleich breit und hoch. Mit Sperre bleibt das Zielformat erhalten. |
| Außerhalb beginnen und hineinziehen. Dann innen neu ansetzen und über den Rand hinausziehen. | Der erste Kontakt bleibt wirkungslos. Der innen begonnene Kontakt zielt ohne Klick und endet am Rand des PC-Ziels. |
| Bei aktivierter linker Taste den zweiten Finger kurz aufsetzen. Danach länger halten, mit dem ersten Finger ziehen und beide Abhebereihenfolgen testen. Einen dritten Finger hinzunehmen. | Ein kurzer Tap löst genau einen Klick aus. Beim Halten bleibt die Taste bis zum ersten Abheben gedrückt. Der dritte Finger erzeugt keinen weiteren Klick. Der verbleibende Zielfinger kann weiterfahren, der verbleibende zweite Finger übernimmt den Cursor nicht. |
| Während des Haltens Android in den Hintergrund schicken, das USB-Kabel ziehen, das WLAN unterbrechen oder am PC den Transport wechseln. Danach beide Apps neu starten. | Die Taste wird freigegeben, bei einem stillen Verbindungsverlust nach rund drei Sekunden. Transport, Area, Monitor, Ausschnitt und Taste bleiben gespeichert. Das bekannte Gerät verbindet sich erneut. |
| Ohne Kabel über WLAN verbinden. Ein Ladekabel einstecken. Anschließend am PC **USB** wählen und ein Datenkabel anschließen. | WLAN bleibt beim Laden aktiv. Beim Umschalten endet die alte Sitzung. Mit freigegebenem ADB stellt sich die USB-Verbindung automatisch her. |
| In osu! zunächst **Empfindlichkeit 1×** und **Raw Input aus** setzen. Für die Fingergeste Maustasten im Spiel zulassen. Kreise antippen und bei einem Slider halten. Danach die Geste in der Tray-App deaktivieren und über die PC-Tastatur spielen. | Zielen, kurzer Klick und Halten funktionieren im Spiel. Im Tastaturmodus übernimmt das Handy ausschließlich das Zielen. |

Starte osu! und Empfänger in derselben normalen Benutzersitzung. Windows berücksichtigt bei `SendInput` die Berechtigungsstufen beider Prozesse. [osu!-Eingabeeinstellungen](https://osu.ppy.sh/wiki/en/Client/Options#mouse) · [Microsoft zu SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)

## Prüfstand vom 19. November 2026

| Tatsächlich ausgeführt | Ergebnis |
| --- | --- |
| Windows-Release mit Visual Studio/MSBuild und .NET-10-SDK | Tray-App und Testprogramm gebaut. |
| Android-Debug-Build, APK-Signatur, fünf JVM-Gestentests und Android Lint | APK gebaut, v2-Signatur gültig, fünf Tests bestanden. Lint meldet 0 Fehler und 6 Hinweise zu SDK-/Gradle-Versionen, Querformat, Zertifikatsprüfung und der Touchansicht. |
| Java-Sitzungscode der App gegen den C#-Empfänger über lokale TCP-/UDP-Sockets | Erster Kontakt, schneller Klick, Halten, Live-Konfiguration, Codevergleich, gespeicherte Kopplung, Socket-Abbruch und Heartbeat-Verlust geprüft. Tastenereignisse wurden aufgezeichnet. |
| Isolierte Windows-Eingabe- und Geometrietests | Vertauschte Paket-Reihenfolge, veraltete Positionen, Abbruch, Tastaturmodus, negativer Desktopursprung, Ränder, Quadrat und gespeicherte Einstellungen geprüft. |
| Reguläre Listener und Transportwahl | Zweite gleichzeitige Sitzung abgewiesen. Wechsel USB → WLAN → USB während eines gehaltenen Klicks mit Freigabe bestanden. |
| WinForms-Oberfläche | Fensterstart, Verschieben in der Vorschau, Quadrat, Monitorwahl, numerischer Ausschnitt, Schließen ins Tray und Speichern geprüft. |
| Echtes Windows-`SendInput` mit anschließendem Auslesen der Cursorposition | Auf drei Monitoren alle 15 Punkte pixelgenau getroffen. 3440×1440 bei (0, 0), 1920×1080 bei (−1920, 361) und 1920×1080 bei (3440, 361). Alle drei liefen bei 100 % Skalierung. |

**Am Gerät offen** sind der komplette Weg vom Finger auf dem Galaxy S23 über ein echtes USB-Kabel bzw. den Heimrouter, Abziehen und Wiedereinstecken, unterschiedliche DPI-Skalierungen sowie Zielen, Halten und Latenz in osu!. Beim Prüflauf meldete ADB kein angeschlossenes Gerät.

Die wiederholbaren Tests laufen im Repo-Stamm mit folgenden Befehlen. Der Java/C#-Test verwendet das JDK aus `JAVA_HOME`, Android Studio oder dem `PATH`. Für `--transports` die Tray-App vorher beenden, damit ihre Ports für den Test frei sind. Ohne diesen Schalter genügen freie lokale Testports.

```powershell
.\android\gradlew.bat -p android :app:testDebugUnitTest :app:lintDebug
dotnet run --project .\windows\OsuTouchpad.Tests -c Release -- --transports
```

## Hintergrund austauschen

Das für dieses Projekt mit ImageGen erzeugte Panoramabild wird auf dem Handy und in der Windows-Vorschau zusätzlich gedimmt. Zum Austauschen die PNG ersetzen und beide Apps neu bauen. [Hintergrunddatei](android/app/src/main/res/drawable-nodpi/playfield.png)

<details>
<summary>Bildprompt</summary>

```text
Use case: stylized-concept. An original ultrawide 20:9 landscape wallpaper for a dimmed rhythm game touch tablet, atmospheric anime background painting. A still volcanic lake below layered, sharply silhouetted alpine ridges at blue hour, a slim crescent and a few stars in a deep indigo-to-dusty-mauve sky, one subtle long pink meteor above the horizon. Restrained dusk light, rich plum shadows, delicate reflections, clean painterly shapes. Compose quiet dark negative space across the center so a bright red touch-area frame can be overlaid in the app. Edge-to-edge landscape illustration. No interface, no frame, no lettering, no logo, no characters.
```

</details>
