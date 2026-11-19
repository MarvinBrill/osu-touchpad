# osu! touchpad

**English** · [Deutsch](README.de.md)

Use an Android phone as an absolute-positioning tablet for osu! on Windows 11. Aim with one finger while looking at the PC monitor. Tap with the PC keyboard or a second finger on the phone. All settings are in the Windows system tray app.

## Build and run

**Windows.** Open the solution in Visual Studio 2026 with the **.NET desktop development** workload installed. Set `OsuTouchpad` as the startup project. To build and run from PowerShell, use the .NET 10 SDK and run these commands from the repository root. [Visual Studio solution](windows/OsuTouchpad.sln)

```powershell
dotnet build .\windows\OsuTouchpad.sln -c Release
& '.\windows\OsuTouchpad\bin\Release\net10.0-windows\OsuTouchpad.exe'
```

The settings window opens at startup. Closing it leaves the receiver running in the system tray. Double-click the tablet icon to reopen settings, or choose **Beenden** (Exit) from its context menu to stop the receiver. To run the app on another Windows PC, copy the entire output folder and install the .NET 10 Desktop Runtime.

**Android.** Open `android/` in Android Studio. The Java app uses Android SDK Platform 34, Build Tools 36.0.0 and the included Gradle wrapper. Select your authorized Galaxy S23 and choose **Run 'app'**. The app supports Android 8.0 (API 26) and later. To build and install from the repository root using Android Studio’s default Windows paths, run the commands below. [Android Gradle plugin compatibility](https://developer.android.com/build/releases/agp-9-1-0-release-notes)

```powershell
$env:JAVA_HOME = Join-Path $env:ProgramFiles 'Android\Android Studio\jbr'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
.\android\gradlew.bat -p android :app:assembleDebug
& "$env:ANDROID_HOME\platform-tools\adb.exe" -d install -r .\android\app\build\outputs\apk\debug\app-debug.apk
```

The installable debug APK is at `android/app/build/outputs/apk/debug/app-debug.apk`. Once installed, open **osu! touchpad** on the phone.

## First-time setup

### USB

1. Enable Developer options and **USB debugging** on the S23. Unlock the phone, connect it with a USB data cable and accept the RSA authorization prompt for this PC. [ADB and device authorization](https://developer.android.com/tools/adb)
2. Select **USB** in the Windows app. The receiver looks for ADB in the Android SDK and on `PATH`, detects the authorized phone and sets up `adb -s <serial> reverse tcp:27183 tcp:27183`.
3. The phone connects to `127.0.0.1:27183`. Windows shows the device name and **Verbunden** (Connected). The status message disappears from the phone. For later sessions, open both apps and plug in the cable.

If Windows keeps searching, check that **Android SDK Platform-Tools** is installed in the SDK Manager. For a custom SDK location, set `ANDROID_HOME` or add `platform-tools` to `PATH`, then restart the receiver. Once authorized, the S23 should appear as `device` in the output of `adb devices -l`.

### Wi-Fi

1. Select **WLAN** (Wi-Fi) on the PC. If Windows Firewall prompts you, allow access on **private networks**. The phone and PC must share the same local IPv4 broadcast network. The PC can connect to the router over Ethernet.
2. On the first connection, compare the six-digit codes on the phone and PC. If they match, choose **Koppeln** (Pair) on the PC. The phone will find the paired PC automatically on later launches, even if its IP address changes.
3. With the cable unplugged, the phone starts Wi-Fi discovery immediately. With a cable connected, it tries ADB first and falls back to Wi-Fi if the receiver does not respond. Plugging in a charging cable during an active Wi-Fi session keeps that session connected.

| Purpose | PC port | Data |
| --- | --- | --- |
| USB | TCP 27183, loopback only | ADB reverse tunnel carrying positions and button events in one ordered stream |
| Wi-Fi discovery | UDP 27185 | IPv4 broadcast request and a unicast reply from the PC |
| Wi-Fi session | TCP 27184 | TLS connection for pairing, configuration, aim start and end, button presses and releases, and heartbeats |
| Wi-Fi movement | UDP 27184 | Absolute position, configuration revision, gesture phase, sequence number and a session-specific HMAC |

If discovery fails, check the inbound ports in the Windows Firewall private profile and the router’s client isolation setting. Guest networks often isolate Wi-Fi clients from Ethernet devices. The receiver accepts one active input session at a time over the selected transport.

## Configure the touch area and target

- In **Ziel & Eingabe** (Target & input), select the monitor where you play. To map to a rectangle, clear **Gesamten Monitor verwenden** (Use entire monitor). Left and top are offsets in physical pixels from that monitor’s top-left corner. Windows accounts for the monitor’s position and scaling on the virtual desktop.
- In **Touchfläche** (Touch area), drag inside the red frame to move it. Drag an edge to resize one dimension or a corner to resize both. The four percentage fields let you adjust the area in steps of 0.01%. The phone draws the frame and reads touches in the same coordinate system. A smaller area means less finger travel.
- Choose **1:1** for a physical square on the phone, choose another aspect ratio preset or enter the width and height independently. Enable **An Seitenverhältnis des Zielbereichs binden** (Lock to target aspect ratio) to preserve the PC target’s proportions as you resize. The preview uses the dimensions of the connected phone’s touch surface.
- Under **Taste für den zweiten Finger** (Second-finger button), choose the left, right or middle mouse button, mouse button 4 or 5, or **Deaktiviert · PC-Tastatur** (Disabled · PC keyboard). The default is the left mouse button. A touch that starts outside the frame is ignored until that finger lifts.

Changing the mapping ends the current gesture. Lift your fingers and touch down again inside the new frame. Windows saves the settings in `%LOCALAPPDATA%\osu-touchpad\settings.json` and sends them to the phone on every connection. The same folder holds the receiver’s Wi-Fi identity and paired devices. Android keeps its pairing data in private app storage.

## Manual acceptance test

| Step | Expected result |
| --- | --- |
| With the 3440×1440 monitor selected, touch the four corners and the center of the red area. Repeat on a second monitor and with an offset target rectangle. Also try different Windows display scales. | The cursor lands on the corresponding target point as soon as the finger touches down. It stays in place when the finger lifts. |
| Move the frame, drag an edge and a corner, edit a percentage field and select **1:1**. Then enable the aspect ratio lock and drag an edge. | The frame on the phone updates immediately and the new boundary applies to input. The square is as wide as it is tall on the phone. With the lock enabled, resizing preserves the target’s proportions. |
| Start outside the frame and drag into it. Lift, start inside and drag beyond the edge. | The first touch has no effect. The second aims without clicking, and the cursor stops at the edge of the PC target. |
| Select the left mouse button. Aim with one finger and briefly tap with a second. Repeat with the second finger held down while moving the first. Try lifting the fingers in both orders and adding a third finger. | A quick tap produces one click. During a hold, the button releases as soon as either finger lifts. A third finger adds no clicks. If the aiming finger remains, it continues aiming. If it lifts first, the cursor stays in place. |
| While holding the button, try putting the app in the background on Android, unplugging USB, interrupting Wi-Fi and switching transports on the PC. Then restart both apps. | The button releases, including after about three seconds if the connection goes silent. The transport, touch area, monitor, target rectangle and button binding persist. The paired device reconnects. |
| Connect over Wi-Fi with the cable unplugged. Plug in a charging cable. Then select **USB** on the PC and connect a data cable. | Wi-Fi stays connected while charging. Switching transports ends the old session. With ADB authorized, the USB session starts automatically. |
| In osu!, start with **Sensitivity 1×** and **Raw input off**. Enable mouse buttons during gameplay for the two-finger gesture. Tap a circle and hold through a slider. Then disable the gesture in the tray app and play using the PC keyboard. | Aiming, tapping and holding work in-game. In keyboard mode the phone handles aiming and the keyboard handles taps. |

Run osu! and the receiver in the same standard Windows user session. Windows checks the privilege levels of both processes when handling `SendInput`. [osu! input settings](https://osu.ppy.sh/wiki/en/Client/Options#mouse) · [Microsoft SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)

## Verification on November 19, 2026

| Check performed | Result |
| --- | --- |
| Windows Release build with Visual Studio/MSBuild and the .NET 10 SDK | Built the tray app and test executable. |
| Android debug build, APK signature, five JVM gesture tests and Android Lint | Built the APK and verified its v2 signature. All five tests passed. Lint reported 0 errors and 6 warnings about SDK and Gradle versions, landscape orientation, certificate validation and the touch view. |
| The app’s Java session code against the C# receiver over local TCP and UDP sockets | Verified initial touch, quick tap, hold, live configuration, pairing code comparison, saved pairing, socket disconnect and heartbeat loss. Mouse button events were recorded. |
| Isolated Windows input and geometry tests | Verified out-of-order packets, stale positions, cancellation, keyboard mode, a negative desktop origin, edge mapping, a square touch area and saved settings. |
| Production listeners and transport selection | The receiver rejected a second simultaneous session. Switching USB → Wi-Fi → USB during a held click released the button. |
| WinForms interface | Verified window startup, dragging the preview area, the square preset, monitor selection, numeric target bounds, closing to the system tray and saving settings. |
| Windows `SendInput` with cursor position readback | Hit all 15 target points to the pixel across three monitors. The 3440×1440 display was at (0, 0), with 1920×1080 displays at (−1920, 361) and (3440, 361). All three used 100% scaling. |

**Hardware coverage.** ADB reported no attached device during this run. The full path from a finger on the Galaxy S23 through a physical USB cable or home router, unplugging and reconnecting the cable, mixed display scaling, and aiming, holding and latency in osu! remained to be tested on hardware.

To repeat the tests, run the commands below from the repository root. The Java/C# test uses the JDK from `JAVA_HOME`, Android Studio or `PATH`. Close the tray app before running with `--transports` to free its listener ports. Omitting that switch runs the checks on available local test ports.

```powershell
.\android\gradlew.bat -p android :app:testDebugUnitTest :app:lintDebug
dotnet run --project .\windows\OsuTouchpad.Tests -c Release -- --transports
```

## Replace the background

The phone and the Windows preview both dim the panorama generated for this project with ImageGen. To use your own background, replace the PNG and rebuild both apps. [Background image](android/app/src/main/res/drawable-nodpi/playfield.png)

<details>
<summary>Image prompt</summary>

```text
Use case: stylized-concept. An original ultrawide 20:9 landscape wallpaper for a dimmed rhythm game touch tablet, atmospheric anime background painting. A still volcanic lake below layered, sharply silhouetted alpine ridges at blue hour, a slim crescent and a few stars in a deep indigo-to-dusty-mauve sky, one subtle long pink meteor above the horizon. Restrained dusk light, rich plum shadows, delicate reflections, clean painterly shapes. Compose quiet dark negative space across the center so a bright red touch-area frame can be overlaid in the app. Edge-to-edge landscape illustration. No interface, no frame, no lettering, no logo, no characters.
```

</details>
