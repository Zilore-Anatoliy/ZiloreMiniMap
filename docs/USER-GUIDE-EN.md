# ZiloreMiniMap User Guide

Welcome to **ZiloreMiniMap** — a tactical minimap, offline island road navigator, live squad radar, server clock, and survival companion for **SCUM** on Windows 10 and Windows 11.

The app runs strictly as an external window overlay: it never reads SCUM game memory, never modifies game files, and never injects DLLs — keeping it 100% safe for Easy Anti-Cheat (EAC).

---

## 1. Quick Start & Installation

### Choosing Your Download
- **Windows Installer (`ZiloreMiniMap-Setup-v0.0.5.exe`) — Recommended.** Installs cleanly per-user without administrator rights, creates shortcuts, and preserves all your waypoints, routes, zones, and settings across updates.
- **Portable Archive (`ZiloreMiniMap-Portable-v0.0.5.zip`).** Zero installation required: extract the entire archive into any folder (keep the `assets` folder beside the EXE) and run `ZiloreMiniMap.exe`.
- **Install via `winget`:** open Windows Terminal or PowerShell and run `winget install Zilore.ZiloreMiniMap`.

### End User License Agreement (EULA)
During Windows Setup or on first launch of the portable edition, the License Agreement window appears. Check the acceptance box and click **Accept & Continue**. If you already accepted during Setup, you will not be asked again. You can review the agreement anytime from the system tray or **Settings → General**.

### Required SCUM Configuration (30 Seconds)
1. **Borderless Display Mode:** in SCUM open *Settings → Video* and switch from Fullscreen to **Windowed Fullscreen (Borderless)**. Exclusive fullscreen prevents Windows from showing external overlays.
2. **Coordinate Copy Hotkey:** in SCUM *Controls*, confirm **Copy Location** is bound to `Num /` (NumPad Divide).
3. **Tenkeyless (No Numpad) Keyboards:** open the ZiloreMiniMap Setup Wizard, choose any comfortable key, and bind that same key to **Copy Location** in SCUM.

### Status Dot Colors (HUD & Tray)
- **Green dot** — live GPS coordinates are arriving smoothly.
- **Amber dot** — tracking is temporarily paused (in-game chat, inventory, menu, or settings open). Press `Esc` to close SCUM chat and tracking resumes automatically.
- **Gray dot** — waiting for SCUM or the first coordinate sample.

---

## 2. Minimap (HUD) & Tactical Map (M)

### In-Game Minimap HUD
- **Shape & Opacity:** switch between a circular compass radar (N/S/E/W) or square minimap. Customize size, screen position, and glass opacity under **Settings → Appearance**.
- **Moving the HUD:** use the HUD placement control in Settings to drag the minimap to any corner — your placement is saved automatically.
- **Smooth Auto-Zoom:** zooms in automatically on foot and zooms out smoothly while driving or flying without FPS drops.
- **Status Bar (Above or Below HUD):** shows current sector (`C2`), nearest landmark, turn arrow with live distance, ETA, in-game & real-world clock, temperature, speed, and survival bars.
- **Danger Zone Alert:** the HUD border turns red automatically when approaching a marked danger/PvP zone.

### Full Tactical Map (Key M)
Press `M` in game (or click the tray icon) to open the full tactical map:
- **Camera Controls:** mouse wheel to zoom, left-drag to pan, `0` to reset zoom to 1.0×, `L` to lock/unlock zoom.
- **Set Destination:** right-click anywhere on the map (bunker, town, POI, or custom point) and pick **Navigate Here**.
- **Distance & Bearing Ruler (`K`):** press `K` on the map and click points to measure exact distance and compass bearing. Press `Esc` to exit ruler mode.
- **Automatic Death Markers:** when SCUM shows the death screen, ZiloreMiniMap saves your last known coordinate with an elapsed timer. Markers clear automatically once you return to your body and move away (or remove them manually from the right rail).
- **GPU Hardware Acceleration (DirectTerrain):** for 2K / 4K 144Hz monitors, enable *Settings → Appearance → Hardware-accelerated M map (GPU)* and restart the app.

---

## 3. Offline Road Navigator, Multi-Stop Routes & Voice

### 100% Offline Road Routing
ZiloreMiniMap calculates routes across the entire SCUM road network offline. Remaining road distance and ETA count down smoothly meter-by-meter as you travel.

- **Road Modes (Key `G`):** *Quickest* (prefers paved roads), *Shortest* (minimum distance), and *Paved Only*.
- **Danger Zone Avoidance (Key `B`):** Off / Avoid All Zones / Avoid Danger & PvP Zones.
- **Turn-by-Turn Cues:** within 220 meters of a junction, the HUD displays a directional turn arrow (`↱`, `↰`, `↻`) and live distance.

### Multi-Stop Routes & Library (Key F9)
1. Open the **Routes** window (`F9` on the map or from the tray menu).
2. Add up to **16 stops** via map right-click, "Add Target", or "Add Current Position".
3. Click **Optimize** (`O`) to automatically reorder stops for the shortest trip, or use **Reverse** and **Loop**.
4. Save your favorite loot runs to the Route Library for 1-click recall.

### Sharing Routes & Waypoints on Discord
- **Share Route (`Ctrl+Shift+C`):** copies a compact `ZLR-ROUTE:...` code. Paste it in Discord so teammates can import the exact route.
- **Share Waypoints:** right-click the map to export or import custom waypoint packs via `ZLR-WP:...` codes.

### Offline Voice Navigation (Key V)
Enable voice guidance under **Settings → Navigation** or press `V` on the tactical map. Choose your voice pack, volume, and audio output device. Going off-route triggers an automatic reroute announcement.

---

## 4. Aviation Mode & Flight Navigation

Above **110 km/h** (taking off in an aircraft), ZiloreMiniMap switches to Aviation Mode automatically (or toggle manually with `F` on the map):
- **Direct Air Course:** disables road snapping and draws a direct flight line with true bearing and bank cue (`←`, `●`, `→`).
- **Flight HUD Telemetry:** altitude (`m`), vertical climb/descent rate (`↑+` / `↓-` m/s), ground speed (`km/h`), and crosswind drift angle.
- **Runways & Top of Descent (TOD):** displays island runways on the map and alerts you when to begin descending toward your destination.

---

## 5. Live Squad Radar

### Encrypted Relay Groups (Recommended)
1. Open **Squad** in the tactical sidebar or Routes window.
2. Click **Create Group** and pick a name — the app generates a private `MM1-...` invite code.
3. Share the code with teammates in Discord; they click **Join** and paste it.
4. All positions, pings, and routes are end-to-end encrypted with your group key over TLS 1.3.

### Direct P2P Links
If you prefer direct peer-to-peer hosting without a relay, use Direct Host mode (includes optional automatic UPnP and Windows Firewall rule setup).

### Squad Features
- **Live Connection Quality:** green = online, amber = delayed, gray = offline.
- **Tactical Pings with Countdown:** right-click on the map → Ping (Enemy, Loot, Rally, Danger). Every ping shows a live 60-second countdown for the whole squad.
- **Rally Point:** "Rally Here" drops a squad rally ping on your position so teammates can route directly to you in one click.
- **Squad Leader & Follow Mode:** assign a star badge to the squad leader, follow a moving teammate, and toggle visibility per player.

---

## 6. Server Clock, Climate, Survival & Zilore AI

### Server Clock & Climate (Key F8)
- Pick your server's day/night cycle speed (`1x`, `3.84x Official`, `4x`, `6x`, `8x`, or custom) and set the current in-game time — the clock stays synced and tracks sunrise/sunset countdowns.
- **Climate Model:** estimates ambient temperature from map coordinates, elevation, and time of day, warning you about hypothermia risk.

### Survival Tracker (Key F7)
Tracks health, energy, and hydration with optional passive HUD bar scanning and displays concise survival recommendations when vitals drop.

### Zilore AI Route Assistant (Key F6)
Plan multi-stop trips using natural language in English or Russian:
- Examples: *"gas station -> trader 5 km"*, *"nearest bunker B3"*, *"police + pharmacy -> gas loop"*.
- Learns your real average foot, car, boat, and aircraft speeds for accurate ETA predictions.
- *The core suite is 100% free; extended POI database filters and Zilore AI unlock via optional personal offline access keys.*

---

## 7. Custom Zones, Quick Layers & POI Search

### Instant POI Search & Filtering
- Use the **Search POIs** box in the right tactical sidebar to filter categories instantly.
- Press `Ctrl+F` for global search across bunkers, towns, custom waypoints, and map grid sectors (such as `D4` or `Samobor`).
- Smart clustering groups dense markers when zoomed out so the map always stays readable.

### Custom Server Zones & Layers (Alt+1–5)
- **Importing Zones:** open the Zone Wizard or double-click any `.tsv` zone pack in Windows to import it into its own layer (automatic import from server map screenshots is also supported).
- **Quick Layers:** toggle custom zone layers live in game using `Alt+1` through `Alt+5`.

---

## 8. Settings, Themes, Backup & Auto-Updates

### Windows 11 Appearance & Themes
- **4 Instant Live Themes (Key `T`):** System (matches Windows), Dark, pure black OLED, and Light — zero restarts required.
- **Native Typography:** uses Windows 11 **Segoe UI Variable** (falling back seamlessly to Segoe UI on Windows 10) with enforced ClearType across 100–200% DPI displays.
- **Adaptive Day/Night Icons:** tray and window icons adapt automatically to light or dark Windows taskbars.

### Auto-Launch with SCUM
Enable **Launch with SCUM** under *Settings → General* so ZiloreMiniMap starts automatically when you launch SCUM and exits cleanly when you close the game.

### Backup, Updates & Privacy
- **1-Click Profile Backup:** open *About → Support* to export a complete ZIP backup of your settings, waypoints, routes, and zones to your Desktop and restore it anytime.
- **Signed Auto-Updates:** checks official channels (`minimap.zilore.app` and GitHub Releases) with cryptographic signature and SHA-256 verification while preserving all user data.
- **Privacy-Safe Logs:** diagnostic ZIP archives automatically redact Windows usernames, access keys, and tokens.

---

## 9. Server Admin Tools

The Admin Suite is disabled by default and intended strictly for server administrators.
- **Admin Console:** quick access to saved commands and relative teleport (forward/back/up/down by meters).
- **Player Directory:** import `#ListPlayers true` from clipboard, search by name or SteamID, show online players on the map, and teleport quickly.
- **Windows 11 Player Profile Window:** Overview, Vehicles, Flags, and Squad tabs with live Steam profile lookup, map markers for vehicles/flags, and 1-click clipboard report import.
- **Spawn Cart:** visual item browser, modifier editor (health, uses, weight, keycard sector), and saved kit database.

---

## 10. Hotkey Reference & Troubleshooting

### Tactical Map Hotkeys
- `M` — open / close tactical map (synced with SCUM)
- `Ctrl+F` — global search for places, bunkers, or grid sectors
- `F1` — quick hotkeys & routes cheat sheet (`Shift+F1` — full User Guide)
- `F6` — Zilore AI route assistant
- `F7` — survival tracker
- `F8` — server clock & climate
- `F9` — routes & squad manager
- `K` — distance & bearing ruler (`Esc` to exit)
- `F` — toggle aviation mode
- `V` — toggle voice navigation
- `G` — cycle road routing mode (Quickest / Shortest / Paved)
- `B` — cycle danger zone avoidance
- `O` — optimize multi-stop route order
- `N` — skip active route stop
- `X` — clear active route
- `L` — lock / unlock tactical zoom
- `H` — collapse / expand sidebar
- `T` — cycle UI theme (Dark / OLED / Light / System)
- `Alt+1` … `Alt+5` — toggle custom zone layers
- `+` / `-` / `0` — zoom in / zoom out / reset zoom to 1.0×
- `Ctrl+Z` / `Ctrl+Y` — undo / redo route edit
- `Ctrl+Shift+C` — copy `ZLR-ROUTE` share code

### Quick Troubleshooting
- **Minimap not visible over SCUM:** switch SCUM *Settings → Video* to **Windowed Fullscreen (Borderless)**.
- **Position stopped updating:** confirm SCUM's `Copy Location` (`Num /`) binding and press `Esc` to ensure SCUM chat is closed.
- **Windows SmartScreen warning on Setup:** download only from `minimap.zilore.app` or our official GitHub, verify SHA-256 against `SHA256SUMS.txt`, and click *More info → Run anyway*.
- **Need help or found a bug?** Open *About → Export Log* and contact `support@zilore.app` or join the **SCUM GRAND** Discord.
