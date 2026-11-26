using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    public sealed partial class MapWindow {
        DateTime lastHomeAction = DateTime.MinValue;

        DateTime lastDeleteAction = DateTime.MinValue;

        DateTime lastEndAction = DateTime.MinValue;

        DateTime lastInsertAction = DateTime.MinValue;

        DateTime lastPgUpAction = DateTime.MinValue;

        DateTime lastPgDnAction = DateTime.MinValue;

        void FocusSettingsShortcut() {

            if((DateTime.UtcNow - lastHomeAction).TotalMilliseconds < 450) return;

            lastHomeAction = DateTime.UtcNow;

            ShowSettings();

        }

        void TriggerZoneSearch() {

            if((DateTime.UtcNow - lastDeleteAction).TotalMilliseconds < 450) return;

            lastDeleteAction = DateTime.UtcNow;

            ShowZoneSearch();

        }

        void TriggerToggleOverlay() {

            if((DateTime.UtcNow - lastEndAction).TotalMilliseconds < 450) return;

            lastEndAction = DateTime.UtcNow;

            ToggleOverlay();

        }

        DateTime lastDeathMarkerAction;
        bool DeathMarkerKeyAvailable() {
            var bindings=CurrentAppControls();
            return bindings.Key(AppControlAction.DeathMarker)!=0
                && bindings.BindingError(scumMapKey,scumMapScanCode,scumChatKey,scumChatScanCode,scumCopyKey,scumCopyScanCode)==null;
        }
        void TriggerDeathMarker() {
            if(IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(TriggerDeathMarker)); return; }
            if(!HotkeyContextAllowed() || adminCommandBusy || !DeathMarkerKeyAvailable()) return;
            if((DateTime.UtcNow-lastDeathMarkerAction).TotalMilliseconds<450) return;
            lastDeathMarkerAction=DateTime.UtcNow;
            if(position!=null) SaveDeathMarker(position,false);
        }
        bool SaveDeathMarker(Position deathPosition,bool automatic) {
            PointF point=ToMap(deathPosition);
            if(float.IsNaN(point.X) || float.IsNaN(point.Y) || point.X<0 || point.X>1 || point.Y<0 || point.Y>1) return false;
            // Preserve the captured point across subsequent coordinate updates and respawns.
            DateTime deathTimeUtc=DateTime.UtcNow;
            var marker=new MapZone { Name=Localization.Get("DeathName")+" "+deathTimeUtc.ToString("o",CultureInfo.InvariantCulture),DeathCreatedUtc=deathTimeUtc,
                Layer=MapZone.DeathMarkerLayer, Category=ZoneCategory.Custom,
                Argb=Color.FromArgb(255,225,210,190).ToArgb(), Points=new[]{point} };
            var candidate=new List<MapZone>(zones);
            candidate.RemoveAll(z=>z.IsDeathMarker && z.DistanceTo(point)<0.00001);
            candidate.Add(marker);
            if(!TryCommitZonesCore(candidate,null,automatic)) return false;
            note=Localization.T("WaypointSaved",marker.DisplayName);
            SettingsChanged();
            RenderOverlay();
            return true;
        }

        void TriggerPin() {
            if(IsHandleCreated && InvokeRequired) {
                BeginInvoke(new Action(TriggerPin));
                return;
            }
            if(panelOpening || searchOpen) return;
            if((DateTime.UtcNow - lastInsertAction).TotalMilliseconds < 450) return;
            lastInsertAction = DateTime.UtcNow;

            if(position==null) return;

            PointF mapPt = ToMap(position);

            MapZone existing = null;

            if(zones != null) {

                foreach(MapZone z in zones) {

                    if(z != null && z.Category==ZoneCategory.Custom && z.Points!=null && z.Points.Length>0) {

                        double dx=z.Points[0].X-mapPt.X, dy=z.Points[0].Y-mapPt.Y;

                        if(Math.Sqrt(dx*dx+dy*dy)<0.005) { existing=z; break; }

                    }

                }

            }

            if(existing!=null) {

                string displayName=existing.DisplayName;

                if(ConfirmWaypointRemoval(displayName,null)) {

                    var candidate=new List<MapZone>(zones); candidate.Remove(existing);
                    if(!TryCommitZones(candidate,null)) return;
                    pin=null;
                    if(searchTarget==existing) { searchTarget=null; activeRoute=null; routeGeneration++; lastRouteTarget=null; }

                    note=Localization.T("WaypointDeleted",displayName);

                    SettingsChanged();

                    RenderOverlay();

                }

                return;

            }

            SaveCustomWaypoint(mapPt,new Position { X=position.X, Y=position.Y, Z=position.Z, Yaw=position.Yaw });

        }

        void ChangeRouteColor() {
            using(ColorDialog dlg = new ColorDialog { Color = routeGuidanceColor, FullOpen = true, AnyColor = true }) {
                if(RunUiModal(() => dlg.ShowDialog()) == DialogResult.OK) {
                    routeGuidanceColor = dlg.Color;
                    SettingsChanged();
                    RenderOverlay();
                }
            }
        }
        void ChangeHistoryColor() {
            using(var dlg = new ColorDialog { Color = routeHistoryColor, FullOpen = true, AnyColor = true }) {
                if(RunUiModal(() => dlg.ShowDialog()) != DialogResult.OK) return;
                routeHistoryColor = dlg.Color; SettingsChanged(); lastFrameKey = null;
                RenderOverlay();
            }
        }
        void ChangePlayerColor() {
            using(ColorDialog dlg = new ColorDialog { Color = playerConeColor, FullOpen = true, AnyColor = true }) {
                if(RunUiModal(() => dlg.ShowDialog()) == DialogResult.OK) {
                    playerConeColor = dlg.Color;
                    SettingsChanged(); lastFrameKey = null;
                    RenderOverlay();
                }
            }
        }
        void SaveCustomWaypoint(PointF mapPt,Position waypointPosition) {
            if(IsHandleCreated && InvokeRequired) {
                BeginInvoke(new Action(() => SaveCustomWaypoint(mapPt, waypointPosition)));
                return;
            }
            if(mapPt.X<0 || mapPt.X>1 || mapPt.Y<0 || mapPt.Y>1) return;
            if(panelOpening || searchOpen) return;

            Native.ReleaseCapture();
            Cursor.Clip=Rectangle.Empty;

            GameFocusReturn focus=new GameFocusReturn(); focus.Capture();

            bool previousPanelOpening=panelOpening;
            panelOpening=true;

            try {
                using(Form prompt=new Form {
                    ClientSize=new Size(380,180),
                    Text=Localization.Get("WaypointNameTitle"),
                    StartPosition=FormStartPosition.CenterScreen,
                    FormBorderStyle=FormBorderStyle.None,
                    MaximizeBox=false,
                    MinimizeBox=false,
                    TopMost=true,
                    ShowInTaskbar=true,
                    Cursor=Cursors.Default
                }) {
                    OverlayTheme.Frame(prompt, Localization.Get("WaypointNameTitle"), () => prompt.DialogResult = DialogResult.Cancel);
                    Label lbl=new Label { Left=20,Top=64,Width=340,Text=Localization.Get("WaypointNamePrompt"),TabIndex=1,ForeColor=OverlayTheme.InkMuted,Font=OverlayTheme.UiFont(9f) };
                    TextBox txt=new TextBox { Left=20,Top=92,Width=340,TabIndex=0,BackColor=OverlayTheme.Surface,ForeColor=OverlayTheme.Ink,BorderStyle=BorderStyle.FixedSingle,Font=OverlayTheme.UiFont(10f) };
                    Button ok=new Button { Text=Localization.Get("Done"),Left=190,Top=132,Width=80,Height=30,DialogResult=DialogResult.OK,TabIndex=2,BackColor=OverlayTheme.Accent,ForeColor=Color.FromArgb(12,12,12),Font=OverlayTheme.UiFont(9f, FontStyle.Bold),FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand };
                    ok.FlatAppearance.BorderColor=OverlayTheme.AccentHover;
                    Button cn=new Button { Text=Localization.Get("Cancel"),Left=280,Top=132,Width=80,Height=30,DialogResult=DialogResult.Cancel,TabIndex=3,BackColor=OverlayTheme.Surface,ForeColor=OverlayTheme.Ink,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand };
                    cn.FlatAppearance.BorderColor=OverlayTheme.Border;
                    prompt.Controls.AddRange(new Control[]{txt,lbl,ok,cn});
                    prompt.ActiveControl=txt;
                    prompt.AcceptButton=ok; prompt.CancelButton=cn;
                    txt.KeyDown += (s, e) => { if(e.KeyCode == Keys.Enter) { ok.PerformClick(); e.Handled = true; e.SuppressKeyPress = true; } };
                    prompt.Shown += (s, e) => prompt.BeginInvoke(new Action(() => {
                        if(prompt.IsDisposed) return;
                        Native.ReleaseCapture();
                        Cursor.Clip=Rectangle.Empty;
                        Native.ForceForeground(prompt.Handle);
                        prompt.Activate();
                        prompt.ActiveControl=txt;
                        txt.Focus();
                        txt.SelectAll();
                        Cursor.Position=txt.PointToScreen(new Point(txt.ClientSize.Width/2,txt.ClientSize.Height/2));
                        txt.Cursor=Cursors.IBeam;
                    }));
                    IWin32Window owner = UiDialogOwner;
                    if(RunUiModal(() => prompt.ShowDialog(owner))==DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text)) {
                        string name=txt.Text.Trim();
                        if(name.Length>80) name=name.Substring(0,80);
                        MapZone wp=new MapZone { Name=name, Argb=Color.FromArgb(255,0,200,255).ToArgb(), Points=new PointF[]{mapPt}, Category=ZoneCategory.Custom };
                        var candidate=new List<MapZone>(zones); candidate.Add(wp);
                        if(!TryCommitZones(candidate,prompt)) return;
                        pin=waypointPosition;

                        note=Localization.T("WaypointSaved",name);
                        SettingsChanged();
                        RenderOverlay();
                    }
                }
            } catch(Exception ex) {
                Program.LogException("SaveCustomWaypoint", ex);
            } finally {
                panelOpening=previousPanelOpening;
                resumeAfter=DateTime.UtcNow.AddMilliseconds(400);
                next=resumeAfter;
                fallbackKeysArmed=false;
                focus.Restore();
            }
        }

        bool zonesLoadFailed;
        bool TryCommitZones(List<MapZone> candidate,IWin32Window owner) { return TryCommitZonesCore(candidate,owner,false); }
        bool TryCommitZonesCore(List<MapZone> candidate,IWin32Window owner,bool silent) {
            try {
                if(zonesLoadFailed) throw new InvalidDataException(Localization.Get("NoteZonesLoadFail"));
                MapSidecarStore.Save(mapSidecarPaths,candidate);
                zones=candidate;
                try { lastZonesFileWriteTimeUtc=File.GetLastWriteTimeUtc(zonesPath); } catch(IOException) {} catch(UnauthorizedAccessException) {}
                try { lastWaypointsFileWriteTimeUtc=File.GetLastWriteTimeUtc(customWaypointsPath); } catch(IOException) {} catch(UnauthorizedAccessException) {}
                return true;
            } catch(Exception ex) {
                if(!(ex is IOException) && !(ex is InvalidDataException) && !(ex is UnauthorizedAccessException) && !(ex is ArgumentException)) throw;
                note=Localization.T("ZeSaveZonesError",ex.Message);
                Program.LogException("SaveWaypoints",ex);
                if(!diagnosticMode && !silent) ShowEngineAlert(note,Localization.Get("ZoneEditorTitle"),owner);
                return false;
            }
        }

        bool ConfirmWaypointRemoval(string displayName,Form owner) {
            if(IsHandleCreated && InvokeRequired) {
                return uiDispatcher.Invoke(() => ConfirmWaypointRemoval(displayName, owner));
            }
            Native.ReleaseCapture();
            Cursor.Clip=Rectangle.Empty;

            GameFocusReturn focus=new GameFocusReturn(); focus.Capture();

            bool previousPanelOpening=panelOpening;

            panelOpening=true;

            try {

                using(Form prompt=CreateWaypointRemovalPrompt(displayName)) {
                    IWin32Window dialogOwner = owner ?? UiDialogOwner;
                    return RunUiModal(() => prompt.ShowDialog(dialogOwner))==DialogResult.Yes;

                }

            } catch(Exception ex) {
                Program.LogException("ConfirmWaypointRemoval", ex);
                return false;
            } finally {

                panelOpening=previousPanelOpening;

                resumeAfter=DateTime.UtcNow.AddMilliseconds(400);

                focus.Restore();

            }

        }

        internal static Form CreateWaypointRemovalPrompt(string displayName) {
            Form prompt=new Form { Text=Localization.Get("RemoveWaypoint"),ClientSize=new Size(420,170),
                StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false,TopMost=true,ShowInTaskbar=true,Cursor=Cursors.Default };
            OverlayTheme.Frame(prompt, Localization.Get("RemoveWaypoint"), () => prompt.DialogResult = DialogResult.Cancel);
            Label message=new Label { Text=Localization.T("WaypointDeleteConfirm",displayName),Left=20,Top=64,Width=380,Height=50,ForeColor=OverlayTheme.Ink,Font=OverlayTheme.UiFont(9.25f) };
            Button remove=new Button { Text=Localization.Get("RemoveWaypoint"),Left=140,Top=122,Width=150,Height=32,DialogResult=DialogResult.Yes,TabIndex=1,BackColor=OverlayTheme.Accent,ForeColor=Color.FromArgb(12,12,12),Font=OverlayTheme.UiFont(9f, FontStyle.Bold),FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand };
            remove.FlatAppearance.BorderColor=OverlayTheme.AccentHover;
            Button cancel=new Button { Text=Localization.Get("Cancel"),Left=300,Top=122,Width=100,Height=32,DialogResult=DialogResult.Cancel,TabIndex=0,BackColor=OverlayTheme.Surface,ForeColor=OverlayTheme.Ink,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand };
            cancel.FlatAppearance.BorderColor=OverlayTheme.Border;
            prompt.Controls.AddRange(new Control[]{message,remove,cancel});
            prompt.AcceptButton=cancel; prompt.CancelButton=cancel;
            prompt.Shown+=(s,e)=>prompt.BeginInvoke(new Action(()=> {

                if(prompt.IsDisposed) return;

                Native.ReleaseCapture();
                Native.ForceForeground(prompt.Handle);

                prompt.Activate(); prompt.ActiveControl=cancel; cancel.Focus();

                Cursor.Clip=Rectangle.Empty;

                Cursor.Position=cancel.PointToScreen(new Point(cancel.Width/2,cancel.Height/2));

            }));

            return prompt;

        }

        void TriggerZoomIn() {

            if((DateTime.UtcNow - lastPgUpAction).TotalMilliseconds < 120) return;

            lastPgUpAction = DateTime.UtcNow;
            if(fullMapActive) { WpfZoom(true, 1); return; }

            autoZoom=false;

            float step=1f+(zoomStepPercent/100f);

            zoom=Math.Min(maxZoom,zoom*step);

            targetZoom=zoom;

            SettingsChanged();

            lastFrameKey=null;

            terrainKey=null;

            RenderOverlay();

        }

        void TriggerZoomOut() {

            if((DateTime.UtcNow - lastPgDnAction).TotalMilliseconds < 120) return;

            lastPgDnAction = DateTime.UtcNow;
            if(fullMapActive) { WpfZoom(true, -1); return; }

            autoZoom=false;

            float step=1f+(zoomStepPercent/100f);

            zoom=Math.Max(1,zoom/step);

            targetZoom=zoom;

            SettingsChanged();

            lastFrameKey=null;

            terrainKey=null;

            RenderOverlay();

        }

        int inputGeneration;
        bool observedChatPaused;
        void SyncChatState() {
            bool paused=chat.Paused;
            if(paused==observedChatPaused) return;
            observedChatPaused=paused; inputGeneration++;
            if(paused) {
                Native.CancelActiveCopy();
                note=Localization.Get("NoteChatOpen");
                if(fullMapActive) SetFullMap(false);
            } else {
                resumeAfter=DateTime.UtcNow.AddMilliseconds(1200); next=resumeAfter;
                note=Localization.Get("NoteChatClosed");
            }
        }

        bool HotkeyContextAllowed(bool settingsShortcut=false) {

            return !diagnosticMode && !closing && !IsDisposed && IsHandleCreated &&

                !AdminPlayerWorkPaused && !adminCommandBusy && !panelOpening && !shortcutCaptureOpen && !chat.Paused && !inventoryInputLocked &&

                (!SettingsVisible || settingsShortcut) && !searchOpen && AppShortcutWindowFocused(settingsShortcut);

        }

        bool IsWatchedPhysicalKey(int key,int scanCode) {
            if(WpfFieldConsoleRuntime.Active!=null&&WpfFieldConsoleRuntime.Active.IsSettingsToggleKey(key,scanCode))return true;
            if(shortcutCaptureOpen) return false;
            if(PhysicalKeyCapture.Matches(key,scanCode,scumMapKey,scumMapScanCode)
                || PhysicalKeyCapture.Matches(key,scanCode,scumChatKey,scumChatScanCode)) return true;
            AppControlAction action;
            if(TryGetAppControl(key,scanCode,out action)) return true;
            return key==0x09 || key==0x0D || key==0x1B || key==0x6F || key==0xBF;
        }

        bool FallbackKeyPressed(int key) {

            bool down=(Native.GetAsyncKeyState(key)&0x8000)!=0;

            bool pressed=fallbackKeysArmed && down && !fallbackKeyDown[key];

            fallbackKeyDown[key]=down;

            return pressed;

        }

        void ArmFallbackKeys() {

            // Seed the state on focus gain. A key held while Alt-Tabbing back must not

            // fire as a new in-game shortcut.

            for(int key=0;key<fallbackKeyDown.Length;key++)

                fallbackKeyDown[key]=(Native.GetAsyncKeyState(key)&0x8000)!=0;

            fallbackKeysArmed=true;

        }

        void OnGameKey(int key,int scanCode=0) {

            try {

                OnGameKeyCore(key,scanCode);

            } catch(Exception ex) {

                Program.LogException("OnGameKey", ex);

            }

        }

        void OnGameKeyCore(int key,int scanCode=0) {
            TraceShortcutContext("UI received",key,scanCode);
            // Settings remains closable after clicking back into SCUM, even while modal gates are set.
            if(WpfFieldConsoleRuntime.Active!=null&&WpfFieldConsoleRuntime.Active.IsSettingsToggleKey(key,scanCode)
                && !closing&&!IsDisposed&&!keys.ShortcutModifiersDown(true)&&AppShortcutWindowFocused(true)) {
                WpfFieldConsoleRuntime.Active.Handle(AppControlAction.Settings);return;
            }
            if(shortcutCaptureOpen) return;

            // Physical chat transitions still matter while a coordinate copy is being cancelled.

            bool settingsKey=PhysicalKeyCapture.Matches(key,scanCode,settingsShortcutKey,settingsShortcutScanCode);
            bool inputWindowFocused=AppShortcutWindowFocused(settingsKey);
            // Dismiss the visible map before tracking/chat/inventory gates. A
            // stale controller flag must never strand a WPF window on screen.
            if(WpfFieldConsoleRuntime.Active!=null&&!closing&&!IsDisposed&&!panelOpening&&(inputWindowFocused||WpfFieldConsoleRuntime.Active.TacticalStandaloneOpen)
                &&!keys.ShortcutModifiersDown(false)&&WpfFieldConsoleRuntime.Active.TryDismissTactical(key,scanCode,chat.Paused))return;

            if(AdminPlayerWorkPaused || diagnosticMode || closing || IsDisposed || panelOpening || adminCommandBusy || !inputWindowFocused) { inputGeneration++; return; }

            // Use physical hook state: the sampler's injected Ctrl is not a user modifier.

            // Shift is also used for sprinting and must not swallow M or chat transitions.

            bool mapKey=PhysicalKeyCapture.Matches(key,scanCode,scumMapKey,scumMapScanCode);
            bool chatKey=PhysicalKeyCapture.Matches(key,scanCode,scumChatKey,scumChatScanCode) || key==0xBF || key==0x6F || key==0x0D || key==0x1B || key==0x09;

            if(keys.ShortcutModifiersDown(!mapKey && !chatKey)) return;

            bool wasChat=observedChatPaused;
            SyncChatState();

            // Escape belongs to chat first if chat was actively open

            if(wasChat && key==0x1B) return;

            // If chat is open and typing, do not swallow in-game text or trigger hotkeys

            if((chat.Paused || inventoryInputLocked) && key!=0x1B && key!=0x0D) return;

            if(!HotkeyContextAllowed(settingsKey)) return;

            AppControlAction appAction;
            if(!mapKey && TryGetAppControl(key,scanCode,out appAction)) {
                DispatchAppControl(appAction);
                return;
            }
            Action action=null;
            if(mapKey) action=TriggerFullMap;
            else if((key==0x09 || key==0x1B) && fullMapActive) action=()=> { if(WpfFieldConsoleRuntime.Active!=null)WpfFieldConsoleRuntime.Active.CloseTactical();else SetFullMap(false); };

            if(action==null) return;
            if(Native.InputTraceActive) Native.TraceInput("shortcut game action vk="+key+" scan="+scanCode+" map="+mapKey);

            resumeAfter=DateTime.UtcNow.AddMilliseconds(400);

            action();

        }

        internal void Accept(string text) {
            Position p=Position.Parse(text); if(p==null)return;
            Accept(p);
        }
        internal void Accept(Position p) {
            if(p==null)return;

            DateTime now=DateTime.UtcNow;

            PointF currentPt=ToMap(p);
            ClearReachedDeathMarkers(currentPt);

            double nowSec=motionClock.Elapsed.TotalSeconds;

            if(position!=null) {

                PointF oldPt=ToMap(position);

                double dSq=(currentPt.X-oldPt.X)*(currentPt.X-oldPt.X)+(currentPt.Y-oldPt.Y)*(currentPt.Y-oldPt.Y);

                if(dSq>0.000001) previousMapPoint=oldPt;

                double dt=Math.Max(.05,(now-updated).TotalSeconds);

                double distance=DestinationSearch.Metres(currentPt,oldPt);

                double rawSpeed=distance/dt; double speed=(dt>5.0 || rawSpeed>125.0 || rawSpeed<0.25)?0.0:rawSpeed;

                filteredSpeed+=(speed-filteredSpeed)*(1-Math.Exp(-dt/1.2));

                if(speed<0.1 && filteredSpeed<0.4) filteredSpeed=0;

                if(autoZoom) {

                    float high=Math.Max(1,autoZoomMax), low=Math.Min(high,Math.Max(1,autoZoomMin));

                    float vehicleSpeedKmH = (float)(filteredSpeed * 3.6);

                    float desired = vehicleSpeedKmH <= 18f ? high : high - (float)Math.Min(1.0, (vehicleSpeedKmH - 18f) / 80f) * (high - low);

                    if(!fixedCentreReady) { targetZoom=desired; zoom=desired; }

                    else if(filteredSpeed<0.4) { targetZoom=high; }

                    else if(Math.Abs(desired-targetZoom)>0.5f) { targetZoom=desired; }

                }

                previousTime = now;

                motion.Sample(currentPt,p.Yaw,nowSec,false);

            } else {

                motion.Sample(currentPt,p.Yaw,nowSec,true);

            }

            if(zones!=null) {

                foreach(MapZone z in zones) {
                    if(z != null && z.Points != null && z.Points.Length >= 3) {
                        string zLayer = string.IsNullOrWhiteSpace(z.Layer) ? "Default" : z.Layer;
                        if(!showZones || disabledZoneLayers.Contains(zLayer)) continue;
                    }
                    if(z != null && z.Contains(currentPt)) {

                        if(lastVisitedZone!=z && zoneChime) {

                            try { System.Media.SystemSounds.Asterisk.Play(); } catch {}

                        }

                        lastVisitedZone=z;

                        lastVisitedTime=DateTime.UtcNow;

                        break;

                    }

                }

            }

            if(!object.ReferenceEquals(searchTarget,wpfUnsharedTarget)&&DestinationReached(searchTarget,currentPt)) {
                VoiceArrived();

                note=Localization.T("NoteWaypointArrived",searchTarget.DisplayName);

                searchTarget=null;

                activeRoute=null; routeGeneration++;

                lastRouteTarget=null;

                lastFrameKey=null;

            }

            if(!fixedCentreReady) { fixedCentreReady=true; terrainKey=null; }

            position=p; updated=now;
            RecordLocationHistory(currentPt,now);

            if(searchTarget!=null) {
                // Compare this sample with the previous route before a routine refresh can hide a wrong turn.
                if(!diagnosticMode) { nextVoiceUpdate=DateTime.MinValue; TickVoice(now,Native.GameFocused()); }
                UpdateRouteAsync(currentPt,searchTarget);
            }

            RenderOverlay();

        }

        bool FallbackBindingPressed(int key,int scanCode) {
            if(key<=0 || key>=fallbackKeyDown.Length) return false;
            if(PhysicalKeyCapture.Valid(scanCode)) {
                int virtualKey=Native.ScanCodeToVirtualKey(scanCode);
                return virtualKey>0 && virtualKey<256 && FallbackKeyPressed(virtualKey);
            }
            return FallbackKeyPressed(key);
        }

        void RecordLocationHistory(PointF point,DateTime sampledAtUtc) {
            PruneLocationHistory(sampledAtUtc);
            if(!locationHistory) return;
            if(locationHistoryPoints.Count>0) {
                LocationHistoryPoint last=locationHistoryPoints[locationHistoryPoints.Count-1];
                // Bound trail density independently of the coordinate polling frequency.
                if((sampledAtUtc-last.SampledAtUtc).TotalSeconds<1) return;
                double dx=point.X-last.MapPoint.X,dy=point.Y-last.MapPoint.Y;
                if(dx*dx+dy*dy<0.00000004 && (sampledAtUtc-last.SampledAtUtc).TotalSeconds<15) return;
            }
            locationHistoryPoints.Add(new LocationHistoryPoint { MapPoint=point,SampledAtUtc=sampledAtUtc });
            if(locationHistoryPoints.Count>LocationHistoryLimit) locationHistoryPoints.RemoveRange(0,locationHistoryPoints.Count-LocationHistoryLimit);
            locationHistoryRevision++;
            terrainKey=null; lastFrameKey=null;
        }

        void PruneLocationHistory(DateTime now) {
            DateTime cutoff=now.AddMinutes(-locationHistoryMinutes);
            int expired=0;
            while(expired<locationHistoryPoints.Count && locationHistoryPoints[expired].SampledAtUtc<cutoff) expired++;
            if(expired==0) return;
            locationHistoryPoints.RemoveRange(0,expired);
            InvalidateLocationHistory();
        }

        void ClearLocationHistory() {
            locationHistoryPoints.Clear();
            InvalidateLocationHistory();
        }

        void InvalidateLocationHistory() { locationHistoryRevision++; terrainKey=null; lastFrameKey=null; }

        void TriggerFullMap() {
            if((DateTime.UtcNow-lastMToggleTime).TotalMilliseconds<250) return;
            if(!fullMapActive && (chat.Paused || inventoryInputLocked)) return;
            lastMToggleTime = DateTime.UtcNow;

            if(WpfFieldConsoleRuntime.Active!=null) { WpfFieldConsoleRuntime.Active.ToggleTactical(true);return; }
            SetFullMap(!fullMapActive);

        }

        void SetFullMap(bool active) {
            if(WpfFieldConsoleRuntime.Active!=null) { WpfFieldConsoleRuntime.Active.SetTactical(active); return; }
            fullMapActive = active;
            adminControlsCommandReturn = false;
            if(!chat.Paused) chat.Reset();
            if(!fullMapActive) { fullMapZoom = 1.0f; fullMapPan = new PointF(0.5f, 0.5f); }
            AdminControlsMapModeChanged();
            lastFrameKey = null; terrainKey = null;
            note = fullMapActive ? Localization.Get("NoteFullMapOn") : Localization.Get("NoteFullMapOff");
            RenderOverlay();
        }
    }
}
