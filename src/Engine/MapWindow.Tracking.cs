using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    public sealed partial class MapWindow {
        DateTime nextTimingLog=DateTime.MinValue;
        int timingLogBusy;
        int diagnosticWriteBusy;
        double lastTickStarted;
        void LogTrackingTiming(double started,double gap) {
            if(!Program.IsTestBuild || DateTime.UtcNow<nextTimingLog) return;
            nextTimingLog=DateTime.UtcNow.AddSeconds(2);
            if(System.Threading.Interlocked.Exchange(ref timingLogBusy,1)!=0) return;
            string entry=string.Format(CultureInfo.InvariantCulture,
                "{0:o} tick_ms={1:F1} gap_ms={2:F1} sample_age_ms={3:F0} pending={4} pending_ms={5:F0} copying={6} failures={7} chat={8} recent_input={9} interval_ms={10}\r\n",
                DateTime.UtcNow,motionClock.Elapsed.TotalMilliseconds-started,gap,position==null?-1:(DateTime.UtcNow-updated).TotalMilliseconds,
                pending,pending?(DateTime.UtcNow-sent).TotalMilliseconds:0,copyInProgress,failures,chat.Paused,Native.UserTypingOrActive(),Program.TrackingIntervalMs(copyIntervalMs,scumCopyModifierKey));
            string path=Path.Combine(dataFolder,"tracking-timing.log");
            ThreadPool.QueueUserWorkItem(_=> {
                try { if(!File.Exists(path) || new FileInfo(path).Length<1048576) File.AppendAllText(path,entry); }
                catch(IOException) {} catch(UnauthorizedAccessException) {}
                finally { System.Threading.Interlocked.Exchange(ref timingLogBusy,0); }
            });
        }
        volatile bool musicalScumbagOpen;
        int musicProcessCheckBusy;
        DateTime nextMusicProcessCheck=DateTime.MinValue;
        bool intensiveWorkWasPaused;
        void CheckMusicProcess(DateTime now) {
            if(now<nextMusicProcessCheck) return;
            nextMusicProcessCheck=now.AddSeconds(musicalScumbagOpen ? 2 : 5);
            if(Interlocked.Exchange(ref musicProcessCheckBusy,1)!=0) return;
            // Process discovery must not stall the UI or compete with the music input loop.
            ThreadPool.QueueUserWorkItem(_=> {
                try {
                    var processes=Process.GetProcessesByName("MusicalScumbag");
                    try { musicalScumbagOpen=processes.Length>0; }
                    finally { foreach(var process in processes) process.Dispose(); }
                } catch { /* Keep the last known state until discovery succeeds. */ }
                finally { Interlocked.Exchange(ref musicProcessCheckBusy,0); }
            });
        }
        bool UpdateIntensiveWorkPause() {
            if(closing || IsDisposed || resourcesReleased) return true;
            if(IntensiveWorkPaused) {
                if(!intensiveWorkWasPaused) {
                    intensiveWorkWasPaused=true;
                    Native.CancelActiveCopy();
                    inputGeneration++;
                    pending=false;
                    clipboardSnapshotReady=false;
                    fallbackKeysArmed=false;
                }
                if(DateTime.UtcNow>=saveAfter) SaveSettingsAsync();
                SetTimerIntervalMs(250);
                return true;
            }
            if(intensiveWorkWasPaused) {
                intensiveWorkWasPaused=false;
                failures=0;
                next=DateTime.UtcNow;
                lastTickStarted=0;
                lastFrameKey=null;
                SetTimerIntervalMs(100);
            }
            return false;
        }
        void PollMusicPausedInput() {
            // Keep manual controls and copied coordinates usable without automatic
            // copy injection, animation, or repeated overlay rendering.
            bool gameFocused=Native.GameFocused();
            keys.Resync(gameFocused);
            SyncChatState();
            if(adminCommandBusy || closing || IsDisposed) return;
            uint current=Native.GetClipboardSequenceNumber();
            if(current==sequence) return;
            string text;
            if(!Native.TryReadCoordinateClipboard(out text)) return;
            sequence=current;
            Position parsedMusic=Position.Parse(text); if(parsedMusic!=null) Accept(parsedMusic);
        }
        void Tick(object sender,EventArgs args) {
            Debug.Assert(!InvokeRequired,"Sampling timer must run on the WinForms UI thread.");
            if(diagnosticMode || closing || IsDisposed) return;
            if(tickBusy) return;
            CheckMusicProcess(DateTime.UtcNow);
            if(UpdateIntensiveWorkPause()) {
                if(musicalScumbagOpen && !AdminPlayerWorkPaused) PollMusicPausedInput();
                return;
            }
            double tickStarted=motionClock.Elapsed.TotalMilliseconds;
            double tickGap=lastTickStarted==0?0:tickStarted-lastTickStarted;
            lastTickStarted=tickStarted;
            tickBusy=true;
            try {
            DateTime now=DateTime.UtcNow;
            if(now>=saveAfter)SaveSettingsAsync();
            PruneLocationHistory(now);
            if(!Program.IsTestBuild && string.IsNullOrEmpty(Program.LocalUpdateSource)
                && updateCheckSchedule.TryBegin(now,!updateCheckRunning && !adminCommandBusy && !relativeTeleportBusy
                    && !copyInProgress && !Native.CopyInProgress && !chat.Paused && !inventoryInputLocked))
                CheckForUpdates(false,true);
            // Menus, dialogs, Alt-Tab and injected copy input can make a low-level hook
            // miss a matching key-up. Reconcile the hook's transition view with Windows'
            // physical state before it can block sampling or a shortcut.
            bool gameFocused=Native.GameFocused();
            PollDeathBanner(now,gameFocused);
            RefreshDeathLabels(now);
            UpdateInventoryInputGate(now,gameFocused,()=>Native.HasSelectedTopNavigationTab(Native.GetForegroundWindow()));
            keys.Resync(gameFocused);
            SyncChatState();
            TickVoice(now,gameFocused);
            bool ourWindowFocused=Native.IsOurWindow(Native.GetForegroundWindow());
            bool gameOrOurFocused=gameFocused || ourWindowFocused;
            int desiredTickInterval=gameOrOurFocused ? 50 : 120;
            SetTimerIntervalMs(desiredTickInterval);
            bool isAltDown=(Native.GetAsyncKeyState(0x12)&0x8000)!=0 || (Native.GetAsyncKeyState(0xA4)&0x8000)!=0 || (Native.GetAsyncKeyState(0xA5)&0x8000)!=0;
            if(isAltDown) lastAltDownTime=now;
            bool altTabRecent=isAltDown || (now - lastAltDownTime).TotalMilliseconds < 500 || (now - Native.LastAltTabTime).TotalMilliseconds < 500;
            if(!altTabRecent) {
                if(gameFocused && !wasGameFocused) {
                    // Automatically resume sampling when gameplay regains focus.
                    failures=0;
                    next=now; // trigger immediate request
                }
                wasGameFocused=gameOrOurFocused;
            }
            SyncFullMapAdminControls(gameOrOurFocused,keys.PhysicalControlDown || altTabRecent || chat.Paused
                || inventoryInputLocked || searchOpen || panelOpening || SettingsVisible);
            if(gameFocused && Native.IsRightMouseDown()) {
                resumeAfter = now.AddMilliseconds(600);
            }
            if(!gameFocused) inputGeneration++;
            // Alt-Tab hides the overlay above without changing SCUM's map state.
            // Restore the same expanded state when focus returns.
            // Hardware polling fallback for non-text hotkeys in case the low-level hook drops.
            // Do not poll M or Escape here: polling cannot tell whether SCUM's chat box owns the
            // keystroke, while the physical hook can.
            bool shortcutFocused=AppShortcutWindowFocused();
            TraceShortcutState();
            if(shortcutFocused && !adminCommandBusy && !panelOpening && !shortcutCaptureOpen && !SettingsVisible && !searchOpen && !chat.Paused && !inventoryInputLocked && !keys.ShortcutModifiersDown(true)) {
                if(!fallbackKeysArmed) ArmFallbackKeys();
                // Every application binding shares the hook's dispatcher and gates.
                // The SCUM map/chat controls continue to belong to the physical hook.
                foreach(var action in AppControlBindings.AllActions) {
                    int binding=AppControlKey(action);
                    if(binding!=0 && FallbackBindingPressed(binding,AppControlScanCode(action))) {
                        if(Native.InputTraceActive) Native.TraceInput("shortcut polling edge action="+action+" vk="+binding+" scan="+AppControlScanCode(action));
                        DispatchAppControl(action);
                    }
                    if(!HotkeyContextAllowed()) break;
                }
            } else {
                fallbackKeysArmed=false;
            }
            // Cursor visibility is not an authoritative map-open signal.
            if(UpdateIntensiveWorkPause()) return;
            motion.Advance(motionClock.Elapsed.TotalSeconds);
            if(autoZoom && Math.Abs(targetZoom-zoom)>0.001f) {
                float zoomDelta=targetZoom-zoom;
                if(Math.Abs(zoomDelta)>0.008f) {
                    zoom+=zoomDelta*0.16f;
                } else {
                    zoom=targetZoom;
                }
            }
            try {
                uint current=Native.GetClipboardSequenceNumber();
                if(current!=sequence && !adminCommandBusy) {
                    string text;
                    if(!Native.TryReadCoordinateClipboard(out text)) throw new ExternalException("Clipboard is busy.");
                    Position p=Position.Parse(text);
                    if(Native.InputTraceActive && pending) Native.TraceInput("copy clipboard changed parsed="+(p!=null));
                    sequence=current;
                    if(p!=null) {
                        Accept(p); failures=0;
                        if(pending) { responses++; note=Localization.Get("NoteCoordReceived"); }
                        if(pending && clipboardRestoreAllowed && Native.GetClipboardSequenceNumber()==current) {
                            try {
                                if(savedDataObject!=null) Clipboard.SetDataObject(savedDataObject,true,0,0);
                                else Native.TryClearClipboard();
                            } catch {}
                            sequence=Native.GetClipboardSequenceNumber();
                        }
                    }
                    if(p!=null) pending=false;
                    else { clipboardSnapshotReady=false; if(pending) clipboardRestoreAllowed=false; }
                }
                if(pending && !copyInProgress && (now-sent).TotalMilliseconds>Program.CopyResponseTimeoutMs(scumCopyModifierKey)) {
                    if(Native.InputTraceActive) Native.TraceInput("copy response timed out");
                    pending=false; failures++; note=Localization.T("NoteNoCoordsAttempt",failures);
                    if(failures>=3) {
                        next=now.AddMilliseconds(Program.CopyRetryDelayMs(scumCopyModifierKey));
                        note=scumCopyKey==Program.DefaultCopyKey && scumCopyScanCode==0?
                            Localization.Get("NoteCaptureCopyKey"):Localization.T("NoteNoCoordsRetry",Program.CopyRetryDelayMs(scumCopyModifierKey)/1000);
                    }
                }
                if(TrackingEnabled && !pending) {
                    if(chat.Paused)note=Localization.Get("NoteChatOpen");
                    else if(Native.IsRightMouseDown())note=Localization.Get("NoteAdsActive");
                    else if(!keys.Available)note=Localization.Get("NoteChatMonitorUnavailable");
                    else if(!gameFocused) note=Localization.Get("NoteWaitingForeground");
                    else if(Native.KeysBusy(scumCopyModifierKey,scumCopyKey,fullMapActive)) note=Localization.Get("NoteWaitingUserKeys");
                }
                if(TrackingEnabled && keys.Available && !panelOpening && !SettingsVisible && !searchOpen && !chat.Paused && !inventoryInputLocked && !pending && !copyInProgress && !altTabRecent && now>=resumeAfter && now>=next && gameFocused && !Native.KeysBusy(scumCopyModifierKey,scumCopyKey,fullMapActive)) {
                    int interval=Program.TrackingIntervalMs(copyIntervalMs,scumCopyModifierKey);
                    next=now.AddMilliseconds(interval);
                    _ = PerformCopyAsync();
                }
            } catch(ExternalException) { note=Localization.Get("NoteClipboardBusy"); }







            // Routine successes are visible in Settings; avoid synchronous disk I/O every copy cycle.



            bool routine=pending || note==Localization.Get("NoteCoordReceived");



            string diagnostic=routine?Localization.Get("DiagCopyActive"):note;



            if(diagnostic!=lastDiagnostic) {



                lastDiagnostic=diagnostic;



                if(System.Threading.Interlocked.Exchange(ref diagnosticWriteBusy,1)==0) {
                    string line=DateTime.UtcNow.ToString("o")+" "+diagnostic+Environment.NewLine;
                    ThreadPool.QueueUserWorkItem(_=> {
                        try {
                            if(File.Exists(diagnosticPath) && new FileInfo(diagnosticPath).Length>1048576) {
                                string prev=diagnosticPath+".prev";
                                if(File.Exists(prev)) File.Delete(prev);
                                File.Move(diagnosticPath,prev);
                            }
                            File.AppendAllText(diagnosticPath,line);
                        } catch(IOException) {} catch(UnauthorizedAccessException) {}
                        finally { System.Threading.Interlocked.Exchange(ref diagnosticWriteBusy,0); }
                    });
                }



            }







            } catch(Exception ex) {



                Program.LogException("Tick", ex);



            } finally {



                tickBusy=false;
                LogTrackingTiming(tickStarted,tickGap);



                if(closing) {



                    try { BeginInvoke(new Action(Close)); } catch {}



                }



            }



        }

        void BeginCopyRequest() { clipboardRestoreAllowed=true; pending=true; sent=DateTime.UtcNow; }
        void CompleteCopyRequest(CopyResult result) {
            // A response consumed during the chord has already cleared pending.
            if(result!=CopyResult.Sent) pending=false;
        }

        async Task PerformCopyAsync() { try {
            if(IntensiveWorkPaused || adminCommandBusy || copyInProgress || closing || IsDisposed) return;
            copyInProgress = true;
            try {
                if(!clipboardSnapshotReady) {
                    uint beforeCapture=Native.GetClipboardSequenceNumber();
                    IDataObject snapshot=await Native.CaptureClipboardAsync();
                    if(IntensiveWorkPaused || adminCommandBusy || closing || IsDisposed || Native.GetClipboardSequenceNumber()!=beforeCapture) return;
                    savedDataObject=snapshot; clipboardSnapshotReady=true;
                }
                sequence = Native.GetClipboardSequenceNumber();
                if(Native.GameFocused() && !Native.KeysBusy(scumCopyModifierKey, scumCopyKey,fullMapActive) && !Native.IsAltOrTabOrWinDown()) {
                    attempts++;
                    BeginCopyRequest();
                    CopyResult copyResult = await Native.Copy(
                        () => !IntensiveWorkPaused && !closing && !panelOpening && !SettingsVisible && !searchOpen && TrackingEnabled && !chat.Paused && !inventoryInputLocked && DateTime.UtcNow >= resumeAfter,
                        scumCopyModifierKey, scumCopyKey,()=>fullMapActive,scumCopyScanCode,
                        // The delivery hook runs on the input thread. Read only input
                        // gates here; UI/control inspection stays in allowed() above.
                        ()=>!closing && !panelOpening && !searchOpen && !diagnosticMode && !adminCommandBusy
                            && !shortcutCaptureOpen && !chat.Paused && !inventoryInputLocked);
                    CompleteCopyRequest(copyResult);
                    if(pending) note=Localization.Get("NoteAutoCopyActive");
                    if(copyResult != CopyResult.Sent) {
                        if(copyResult == CopyResult.Cancelled) note = Localization.Get("NoteCopyCancelled");
                        else { failures = 3; next = DateTime.UtcNow.AddSeconds(5); note = Localization.T("NoteCopyErrorRetry", Native.CopyError); }
                    }
                }
            } catch(Exception ex) {
                pending=false;
                Program.LogException("ClipboardCopy",ex);
                note = Localization.Get("NoteClipboardBusy");
            } finally {
                copyInProgress = false;
            }
        } catch(Exception ex) { Program.LogException("PerformCopyAsync",ex); } }



    }
}
