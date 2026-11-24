# 📖 ZiloreMiniMap User Guide

A clear, step-by-step guide for everyday **SCUM** players — from first launch to advanced tactical features.

---

## 1. First Launch & Setup Wizard

When you first run `ZiloreMiniMap.exe`, the 5-step **Setup Wizard** opens automatically:
1. **SCUM Checklist:** Confirm SCUM is in *Borderless / Windowed Fullscreen* mode and pick your keyboard layout (full keyboard with Numpad or compact TKL without Numpad).
2. **HUD Appearance:** Choose minimap size, shape (circular radar or square), opacity, theme, and — if you have a 2K/4K monitor — enable **GPU Hardware Acceleration for M map**. Use *Move Minimap* to drag the HUD to any screen corner.
3. **Map Layers & Colors:** Toggle grid labels, borders, zones, POI clustering, and customize player/route colors.
4. **Tracking & Aviation:** Configure max zoom, zoom step, and automatic flight mode detection (`> 110 km/h`).
5. **Done:** All settings are saved automatically. You can reopen the wizard anytime via the **`?`** button on the tactical map.

---

## 2. Minimap (HUD) Overlay

The compact HUD stays visible over SCUM and displays:
* Live player position, view-cone heading, and a 6-second velocity projection vector when driving fast.
* Current grid sector (e.g. `C2`), nearest landmark, distance, and route guidance summary.
* **SCUM Server Time** (`☀` day / `🌙` night), local climate/temperature estimate, and **real-world local clock**.
* **Squad Badge** (`👥`) showing online squadmates, plus passive survival bars when Zilore AI is active.
* **Dynamic Status Bezel:** Cyan = normal live tracking, Amber = paused or stale coordinates, Red = approaching a danger zone (< 150 m).

---

## 3. Tactical Map (`M`) & Offline Road Routing

Press **`M`** (customizable in Settings) to open the full-screen tactical map:
* **Instant Search:** Search towns, bunkers, traders, grid sectors (`B3`), saved waypoints, and custom zones.
* **Road Navigation:** **Right-click** anywhere on the map and select *Set Route*. ZiloreMiniMap calculates an offline road route while avoiding marked danger zones.
* **Multi-Stop Routes (up to 16 stops):** Add multiple stops via right-click. In the route card you can:
  * **Optimize** — automatically reorders stops for the shortest path;
  * **📋 Share Code (`ZLR-ROUTE`)** — copies a compact route code to paste in Discord so teammates load the exact same route.
* **Distance & Bearing Ruler:** Measure exact distances and compass bearings between multiple points via the right-click menu.
* **Live Controls:** Adjust map opacity (`0–100%`), lock zoom (`🔒`), or recenter on your player from the floating bottom toolbar.

---

## 4. Squad Mode (Team Radar)

Open **Squad / Groups** from the sidebar or Settings:
* **Private Relay Groups (Recommended):** Create a group in one click and share the invite code with friends — no router configuration required.
* **Direct P2P Mode:** Encrypted direct peer-to-peer position sharing by IP and port.
* Each squadmate card shows live connection quality (`●` online / `◐` delayed / `○` offline), quick buttons (**Locate**, **Follow**, **Set Leader ★**), and individual visibility pills (live position, GPS, waypoints, deaths, routes).

---

## 5. SCUM Server Clock & Climate

Click the clock icon **`🕒`** on the right edge of the tactical map:
1. Enter your server's current in-game time (`HH : MM`).
2. Pick the server cycle speed preset (`1x`, `3.84x Official`, `4x`, `6x`, `8x`, or custom `TimeOfDaySpeed`).
3. Click **Apply & Sync Clock** — settings persist in `game-clock.ini`.
4. Optionally enable **Passive Sky/Rain Detector (EAC-Safe)** for live weather cues.

---

## 6. Themes, Day/Night Icons & Profile Backup

* **Live Themes:** Open **Settings → Appearance** and choose `System`, `Dark`, `OLED`, or `Light`. Windows, tray icon, taskbar icon, and title-bar badges adapt instantly without restarting.
* **Profile Backup & Diagnostics:** Open **About → Support**:
  * **💾 Backup Profile (ZIP)** saves all settings, waypoints, routes, custom zones, and runways to your Desktop.
  * **♻ Restore from ZIP** imports your saved profile on any PC.
  * **Export Log** creates a privacy-safe diagnostic ZIP to send to [`support@zilore.app`](mailto:support@zilore.app).

---

## 7. Optional Server Admin Suite

Available under **Settings → Tools** (password-protected for SCUM server administrators):
* **Admin Commands Console:** Custom `#` commands, toggle state tracking, and directional relative teleport.
* **Players Database:** Fast lookup and one-click `TeleportTo` / `TeleportHere`.
* **Spawn Cart:** Searchable SCUM item catalog for batched spawn command execution.
