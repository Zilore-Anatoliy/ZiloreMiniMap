#nullable enable
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    // Return focus only when an explicitly opened panel still owns it.
    // Alt-Tab to another application must never be undone.
    sealed class GameFocusReturn {
        IntPtr game;
        uint gamePid;

        internal void Capture() {
            game = Native.GetForegroundWindow();
            if (!Native.IsGameWindow(game)) game = IntPtr.Zero;
            Native.GetWindowThreadProcessId(game, out gamePid);
        }

        internal static bool ShouldRestore(bool validGame, uint targetPid, uint savedPid, uint foregroundPid, uint appPid) {
            return validGame && targetPid == savedPid && foregroundPid == appPid;
        }

        internal void Restore() {
            IntPtr target = game;
            game = IntPtr.Zero;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
            Native.GetWindowThreadProcessId(target, out uint targetPid);
            if (ShouldRestore(Native.IsGameWindow(target), targetPid, gamePid, foregroundPid, (uint)Environment.ProcessId)) {
                Native.SetForegroundWindow(target);
            }
        }
    }

    public sealed class MapMotion {
        public PointF Point;
        public double Yaw;
        PointF start, target;
        double began, duration, lastSample;
        bool initialized;

        public static double HeadingDelta(double from, double to) { return ((to - from + 540) % 360 + 360) % 360 - 180; }

        public void Sample(PointF point, double yaw, double now, bool snap) {
            Advance(now);
            double interval = now - lastSample;
            if (!initialized || snap || interval > 3 || DestinationSearch.Metres(Point, point) > 150) {
                Point = point;
                Yaw = yaw;
            }
            if (initialized && !snap && DestinationSearch.Metres(Point, point) < 0.30) point = Point;
            if (initialized && !snap && Math.Abs(HeadingDelta(Yaw, yaw)) < 1.2) yaw = Yaw;
            start = Point;
            target = point;
            // Heading describes the latest observed view direction, not travel motion.
            // Apply it immediately rather than delaying it behind position smoothing.
            Yaw += HeadingDelta(Yaw, yaw);
            // Reach the latest observed position promptly without predicting beyond it.
            began = now;
            duration = Math.Max(.06, Math.Min(.18, initialized ? interval * .5 : .1));
            lastSample = now;
            initialized = true;
        }

        public bool Advance(double now) {
            if (!initialized) return false;
            double t = Math.Max(0, Math.Min(1, (now - began) / duration));
            PointF next = new PointF((float)(start.X + (target.X - start.X) * t), (float)(start.Y + (target.Y - start.Y) * t));
            bool changed = next != Point;
            Point = next;
            return changed;
        }

        public static void SelfTest() {
            MapMotion m = new MapMotion();
            m.Sample(new PointF(.5f, .5f), 359, 0, false);
            m.Sample(new PointF(.501f, .5f), 1, .25, false);
            m.Advance(.3125);
            if (Math.Abs(m.Point.X - .5005) > .000001 || Math.Abs(m.Yaw - 361) > .001) throw new Exception("Motion midpoint or immediate heading update failed.");
            m.Advance(2);
            if (Math.Abs(m.Point.X - .501) > .000001 || m.Advance(3)) throw new Exception("Motion overshoot or idle redraw.");
            m.Sample(new PointF(.9f, .9f), 45, 3, false);
            if (m.Point.X != .9f) throw new Exception("Teleport must snap.");
            m.Sample(new PointF(.900005f, .9f), 45.5, 4, false);
            if (m.Point.X != .9f || m.Yaw != 45.0) throw new Exception("Stationary micro-jitter must be deadbanded.");
        }
    }

    public sealed class ChatState {
        long state;
        internal long Revision { get { return System.Threading.Interlocked.Read(ref state) >> 1; } }
        internal bool AllowsQueuedShortcut(long revision) { return !Paused && Revision == revision; }

        public bool Paused {
            get {
                if ((System.Threading.Interlocked.Read(ref state) & 1) == 0) return false;
                // An idle chat box still owns typing; only an explicit close releases it.
                return true;
            }
        }

        public void Key(int key, int chatOpenKey = 0x54, int copyKey = 0, int scanCode = 0, int chatOpenScanCode = 0) {
            // Enter submits a message but can leave the chat box open for the
            // next message. Only Escape proves this physical chat session ended.
            if (key == 0x1B) {
                SetPaused(false);
                return;
            }
            // Opening chat via configured chat key, '/' or keypad '/'
            if (PhysicalKeyCapture.Matches(key, scanCode, chatOpenKey, chatOpenScanCode) || ((key == 0xBF || key == 0x6F) && key != copyKey)) {
                SetPaused(true);
                return;
            }
        }

        void SetPaused(bool paused) {
            long before, after;
            do { before = System.Threading.Interlocked.Read(ref state); after = ((before >> 1) + 1) * 2 + (paused ? 1 : 0); }
            while (System.Threading.Interlocked.CompareExchange(ref state, after, before) != before);
        }

        public void Reset() { SetPaused(false); }
    }

    // Hook and polling must share edges: replaying an old Enter after a newer T
    // would clear the chat gate while SCUM's text box is still open.
    public sealed class ChatKeyMonitor {
        readonly bool[] held = new bool[256];

        public bool ObserveSample(int key, bool down, bool focused, ChatState? chat, int openKey, int copyKey = 0, int chatOpenScanCode = 0) {
            // Async state cannot distinguish a physical close from injected admin
            // cleanup or an older held key. Only hook events may release chat.
            // Do not seed held[] here: that would hide the next physical close edge.
            if (key == 0x0D || key == 0x1B) return false;
            return Observe(key, down, focused, chat, openKey, copyKey, 0, chatOpenScanCode);
        }

        public bool Observe(int key, bool down, bool focused, ChatState? chat, int openKey, int copyKey = 0, int scanCode = 0, int chatOpenScanCode = 0) {
            if (key < 0 || key >= held.Length) return false;
            bool fresh = down && !held[key];
            held[key] = down;
            if (!focused || !fresh || chat == null) return false;
            chat.Key(key, openKey, copyKey, scanCode, chatOpenScanCode);
            return true;
        }
    }

    public sealed class PhysicalKeyTransitions {
        readonly bool[] held = new bool[256];
        static readonly int[] ModifierKeys = new int[] {
            0x11, 0xA2, 0xA3, // Ctrl, LCtrl, RCtrl
            0x12, 0xA4, 0xA5, // Alt, LAlt, RAlt
            0x5B, 0x5C,       // LWin, RWin
            0x10, 0xA0, 0xA1  // Shift, LShift, RShift
        };

        public bool IsDown(int key) {
            return key >= 0 && key < held.Length && System.Threading.Volatile.Read(ref held[key]);
        }

        public bool ShortcutModifiersDown(bool includeShift) {
            return IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3) ||
                IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5) || IsDown(0x5B) || IsDown(0x5C) ||
                (includeShift && (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)));
        }

        public bool Update(int key, bool down) {
            if (key < 0 || key >= held.Length) return false;
            bool fresh = down && !IsDown(key);
            System.Threading.Volatile.Write(ref held[key], down);
            return fresh;
        }

        public void Resync(Func<int, bool>? isDown) {
            if (isDown == null) return;
            // A missed key-up must not leave M (or any other shortcut) permanently held.
            for (int key = 0; key < held.Length; key++)
                if (IsDown(key) && !isDown(key)) System.Threading.Volatile.Write(ref held[key], false);
            for (int i = 0; i < ModifierKeys.Length; i++) {
                int key = ModifierKeys[i];
                System.Threading.Volatile.Write(ref held[key], isDown(key));
            }
        }
    }

    // Observes shortcut transitions and activity timestamps. No typed text is collected or suppressed.
    internal static class PhysicalKeyCapture {
        internal static string KeyName(int key) {
            if (key == 0x22) return "PageDown";
            if (key == 0) return Localization.Get("KeyNone");
            if (key == 0xBF) return "/";
            if (key == 0x6F) return "Num /";
            if (key == 0x20) return "Space";
            if (key == 0x11 || key == 0xA2 || key == 0xA3) return "Ctrl";
            if (key == 0x10 || key == 0xA0 || key == 0xA1) return "Shift";
            if (key == 0x12 || key == 0xA4 || key == 0xA5) return "Alt";
            if (key == 0xDC) return "\\";

            string name = ((Keys)key).ToString();
            return string.IsNullOrEmpty(name) ? "VK " + key.ToString(System.Globalization.CultureInfo.InvariantCulture) : name;
        }

        internal static bool IsModifierKey(int key) {
            return key == 0x10 || key == 0xA0 || key == 0xA1 || key == 0x11 || key == 0xA2 || key == 0xA3 ||
                key == 0x12 || key == 0xA4 || key == 0xA5 || key == 0x5B || key == 0x5C;
        }

        static readonly object sync = new object();
        static int lastKey, lastScan;
        static DateTime lastTime;

        internal static void Observe(int key, uint scan, uint flags) {
            int encoded = EncodeScanCode(scan, flags);
            if (key <= 0 || key >= 256 || !Valid(encoded)) return;
            lock (sync) { lastKey = key; lastScan = encoded; lastTime = DateTime.UtcNow; }
        }

        internal static int EncodeScanCode(uint scan, uint flags) { return scan > 0 && scan <= 255 ? (int)scan | ((flags & 1) != 0 ? 0x100 : 0) : 0; }

        internal static bool TryGet(int key, out int scan) {
            lock (sync) {
                scan = lastKey == key && (DateTime.UtcNow - lastTime).TotalSeconds < 3 ? lastScan : 0;
                return scan != 0;
            }
        }

        internal static bool Valid(int scan) { return (scan & 0xFF) > 0 && (scan & 0xFF) <= 255 && (scan & ~0x1FF) == 0; }

        internal static bool Matches(int key, int scan, int configuredKey, int configuredScan) {
            return Valid(configuredScan) && Valid(scan) ? scan == configuredScan : key == configuredKey;
        }

        internal static bool SameBinding(int firstKey, int firstScan, int secondKey, int secondScan) {
            return Valid(firstScan) && Valid(secondScan) ? firstScan == secondScan : firstKey == secondKey;
        }

        internal static bool HasConflicts(int[]? keys, int[]? scans) {
            if (keys == null || scans == null || keys.Length != scans.Length)
                throw new ArgumentException("Key and scan-code lists must match.", nameof(keys));
            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                    if (SameBinding(keys[i], scans[i], keys[j], scans[j])) return true;
            return false;
        }
    }

    internal sealed class MapMouseShield {
        int blocked;

        internal bool Suppress(int message, uint data, bool active, bool inside, bool relevantFocus) {
            int bit = message == 0x201 || message == 0x202 ? 1 : message == 0x204 || message == 0x205 ? 2 : message == 0x207 || message == 0x208 ? 4 : message == 0x20B || message == 0x20C ? ((data >> 16) == 1 ? 8 : 16) : 0;
            bool down = message == 0x201 || message == 0x204 || message == 0x207 || message == 0x20B;
            bool up = message == 0x202 || message == 0x205 || message == 0x208 || message == 0x20C;
            if (up && bit != 0) {
                bool wasBlocked = (blocked & bit) != 0;
                blocked &= ~bit;
                return wasBlocked;
            }
            bool suppress = active && relevantFocus && !inside;
            if (down && bit != 0 && suppress) blocked |= bit;
            return suppress && (bit != 0 || message == 0x20A || message == 0x20E);
        }
    }

    sealed partial class GameKeys : IDisposable {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(IntPtr hook);
        [LibraryImport("user32.dll")] private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr GetModuleHandle(string? name);

        [StructLayout(LayoutKind.Sequential)] struct KeyboardData { public uint key, scan, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MouseData { public int x, y; public uint data, flags, time; public UIntPtr extra; }

        static readonly Func<int, bool> AsyncKeyDownProbe = key => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

        readonly HookProc callback;
        readonly HookProc mouseCallback;
        readonly Action<int, int> action;
        readonly Func<int, int, bool>? shouldDispatch;
        readonly Func<int, int, bool>? sharedGameBinding;
        readonly ChatState? chatState;
        readonly Func<int>? getChatOpenKey;
        readonly Func<int>? getCopyModifier;
        readonly Func<int>? getCopyKey;
        readonly Func<int>? getChatScanCode;
        readonly Func<bool>? inventoryOwnsInput;
        readonly PhysicalKeyTransitions transitions = new PhysicalKeyTransitions();
        readonly bool[] suppressedShortcuts = new bool[256];
        readonly int[] chatProbeKeys = new int[5];
        readonly Action resyncKeyboardAction;
        readonly Action cancelCopyAction;
        readonly Action installKeyboardHookAction;

        IntPtr hook;
        IntPtr mouseHook;
        System.Windows.Forms.ApplicationContext? mouseContext;
        System.Windows.Forms.Control? mouseDispatch;
        readonly System.Threading.Thread mouseThread;
        System.Windows.Forms.ApplicationContext? keyboardContext;
        System.Windows.Forms.Control? keyboardDispatch;
        readonly System.Threading.Thread keyboardThread;
        readonly System.Windows.Threading.Dispatcher uiDispatcher;
        int resyncQueued, cancelQueued;
        DateTime nextHookRefresh = DateTime.MinValue;
        volatile bool disposed;
        volatile bool wasFocused;
        IntPtr sampledGameWindow;
        readonly ChatKeyMonitor chatKeys = new ChatKeyMonitor();

        static long lastMouseTicks, lastUserTicks, lastFreshKeyTicks;
        static DateTime lastMouseAction { get { return new DateTime(System.Threading.Interlocked.Read(ref lastMouseTicks), DateTimeKind.Utc); } set { System.Threading.Interlocked.Exchange(ref lastMouseTicks, value.Ticks); } }
        public static DateTime LastUserInputTime { get { return new DateTime(System.Threading.Interlocked.Read(ref lastUserTicks), DateTimeKind.Utc); } set { System.Threading.Interlocked.Exchange(ref lastUserTicks, value.Ticks); } }
        internal static long PhysicalInputRevision;
        public static DateTime LastFreshKeyDownTime { get { return new DateTime(System.Threading.Interlocked.Read(ref lastFreshKeyTicks), DateTimeKind.Utc); } set { System.Threading.Interlocked.Exchange(ref lastFreshKeyTicks, value.Ticks); } }

        internal static bool UserActiveRecently(int windowMs = 90) {
            DateTime now = DateTime.UtcNow;
            return (now - LastFreshKeyDownTime).TotalMilliseconds < windowMs || (now - lastMouseAction).TotalMilliseconds < 400;
        }

        public bool Available { get { return hook != IntPtr.Zero && mouseHook != IntPtr.Zero; } }
        public bool ShortcutModifiersDown(bool includeShift) { return transitions.ShortcutModifiersDown(includeShift); }
        internal bool PhysicalControlDown { get { return transitions.IsDown(0x11) || transitions.IsDown(0xA2) || transitions.IsDown(0xA3); } }
        internal Action<int, Point>? OnPhysicalMouse;
        internal Func<int, Point, uint, bool>? SuppressPhysicalMouse;

        internal static void ClearInputCooldowns() {
            lastMouseAction = LastUserInputTime = LastFreshKeyDownTime = DateTime.MinValue;
        }

        public GameKeys(
            Action<int, int> action,
            Func<int, int, bool>? shouldDispatch,
            ChatState? chatState = null,
            Func<int>? getChatOpenKey = null,
            Func<int>? getCopyModifier = null,
            Func<bool>? inventoryOwnsInput = null,
            Func<int>? getCopyKey = null,
            Func<int>? getChatScanCode = null,
            Func<int, int, bool>? sharedGameBinding = null) {
            ArgumentNullException.ThrowIfNull(action);
            this.sharedGameBinding = sharedGameBinding;
            this.action = action;
            this.shouldDispatch = shouldDispatch;
            this.chatState = chatState;
            this.getChatOpenKey = getChatOpenKey;
            this.getCopyModifier = getCopyModifier;
            this.inventoryOwnsInput = inventoryOwnsInput;
            this.getCopyKey = getCopyKey;
            this.getChatScanCode = getChatScanCode;
            callback = OnKey;
            mouseCallback = OnMouse;
            uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            installKeyboardHookAction = InstallKeyboardHook;
            resyncKeyboardAction = () => {
                System.Threading.Interlocked.Exchange(ref resyncQueued, 0);
                if (!disposed) ResyncKeyboard(wasFocused);
            };
            cancelCopyAction = () => {
                System.Threading.Interlocked.Exchange(ref cancelQueued, 0);
                if (!disposed) Native.CancelActiveCopy();
            };

            using (var ready = new System.Threading.ManualResetEvent(false)) {
                Exception? startupError = null;
                keyboardThread = new System.Threading.Thread(() => {
                    try {
                        keyboardContext = new System.Windows.Forms.ApplicationContext();
                        keyboardDispatch = new System.Windows.Forms.Control();
                        _ = keyboardDispatch.Handle;
                        InstallKeyboardHook();
                    } catch (Exception error) { startupError = error; }
                    finally { _ = ready.Set(); }
                    if (startupError != null) {
                        if (keyboardDispatch != null) keyboardDispatch.Dispose();
                        if (keyboardContext != null) keyboardContext.Dispose();
                        return;
                    }
                    try { System.Windows.Forms.Application.Run(keyboardContext!); }
                    finally {
                        if (hook != IntPtr.Zero) _ = UnhookWindowsHookEx(hook);
                        hook = IntPtr.Zero;
                        keyboardDispatch?.Dispose();
                        keyboardContext?.Dispose();
                    }
                });
                keyboardThread.IsBackground = true;
                keyboardThread.Name = "MiniMap keyboard input";
                keyboardThread.SetApartmentState(System.Threading.ApartmentState.STA);
                keyboardThread.Start();
                _ = ready.WaitOne();
            }

            // Low-level hooks delay input system-wide until their owning thread pumps
            // messages. Never put the high-frequency mouse hook on the renderer.
            using (var ready = new System.Threading.ManualResetEvent(false)) {
                Exception? startupError = null;
                mouseThread = new System.Threading.Thread(() => {
                    try {
                        mouseContext = new System.Windows.Forms.ApplicationContext();
                        mouseDispatch = new System.Windows.Forms.Control();
                        _ = mouseDispatch.Handle;
                        mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
                    } catch (Exception error) { startupError = error; }
                    finally { _ = ready.Set(); }
                    if (startupError != null) {
                        if (mouseDispatch != null) mouseDispatch.Dispose();
                        if (mouseContext != null) mouseContext.Dispose();
                        return;
                    }
                    try { System.Windows.Forms.Application.Run(mouseContext!); }
                    finally {
                        if (mouseHook != IntPtr.Zero) _ = UnhookWindowsHookEx(mouseHook);
                        mouseHook = IntPtr.Zero;
                        mouseDispatch?.Dispose();
                        mouseContext?.Dispose();
                    }
                });
                mouseThread.IsBackground = true;
                mouseThread.Name = "MiniMap mouse input";
                mouseThread.SetApartmentState(System.Threading.ApartmentState.STA);
                mouseThread.Start();
                _ = ready.WaitOne();
            }
            RefreshHooks();
        }

        void RefreshHooks() {
            if (disposed) return;
            var kd = keyboardDispatch;
            if (kd != null && !kd.IsDisposed) {
                try { _ = kd.BeginInvoke(installKeyboardHookAction); } catch (InvalidOperationException) { }
            }
            nextHookRefresh = DateTime.UtcNow.AddSeconds(30);
        }

        void InstallKeyboardHook() {
            if (disposed) return;
            if (hook != IntPtr.Zero && (Native.CopyInProgress || Native.AdminInputInProgress)) return;
            if (hook != IntPtr.Zero) _ = UnhookWindowsHookEx(hook);
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            int keyboardError = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            if (Native.InputTraceActive)
                Native.TraceInput("hooks keyboard_installed=" + (hook != IntPtr.Zero) + " keyboard_error=" + keyboardError + " mouse_installed=" + (mouseHook != IntPtr.Zero));
            nextHookRefresh = DateTime.UtcNow.AddSeconds(30);
        }

        public void Resync(bool focused) {
            if (disposed) return;
            if (Native.InputTraceActive && focused != wasFocused) {
                Native.TraceInput("game focus=" + focused);
                Native.TraceFocusSecurity(focused);
            }
            // Windows may silently remove a hook after a long UI stall; a non-zero handle
            // alone is not proof it still receives input. Refresh without changing chat state.
            if (!Native.CopyInProgress && !Native.AdminInputInProgress && (DateTime.UtcNow >= nextHookRefresh || (focused && !wasFocused))) RefreshHooks();
            sampledGameWindow = focused ? Native.GetForegroundWindow() : IntPtr.Zero;
            wasFocused = focused;
            if (System.Threading.Interlocked.Exchange(ref resyncQueued, 1) != 0) return;
            var kd = keyboardDispatch;
            if (kd == null || kd.IsDisposed) { System.Threading.Interlocked.Exchange(ref resyncQueued, 0); return; }
            try { _ = kd.BeginInvoke(resyncKeyboardAction); }
            catch (InvalidOperationException) { System.Threading.Interlocked.Exchange(ref resyncQueued, 0); }
        }

        void ResyncKeyboard(bool focused) {
            focused = FocusStillMatches(focused, sampledGameWindow, Native.GetForegroundWindow());
            if (!Native.CopyInProgress && !Native.AdminInputInProgress) {
                transitions.Resync(AsyncKeyDownProbe);
                for (int key = 0; key < suppressedShortcuts.Length; key++)
                    if (suppressedShortcuts[key] && (Native.GetAsyncKeyState(key) & 0x8000) == 0) suppressedShortcuts[key] = false;
            }
            // GetAsyncKeyState includes our injected copy key. The hook already
            // handles physical chat input during copying and ignores injected events.
            if (Native.CopyInProgress || Native.AdminInputInProgress) return;
            int openKey = getChatOpenKey != null ? getChatOpenKey() : 0x54;
            int copyKey = getCopyKey != null ? getCopyKey() : 0;
            int chatScan = getChatScanCode != null ? getChatScanCode() : 0;
            chatProbeKeys[0] = openKey;
            chatProbeKeys[1] = 0xBF;
            chatProbeKeys[2] = 0x6F;
            chatProbeKeys[3] = 0x0D;
            chatProbeKeys[4] = 0x1B;
            for (int pi = 0; pi < chatProbeKeys.Length; pi++) {
                int key = chatProbeKeys[pi];
                if (key < 0 || key >= 256) continue;
                if (key == copyKey && key != openKey && (key == 0xBF || key == 0x6F)) continue;
                bool down = (Native.GetAsyncKeyState(key) & 0x8000) != 0;
                if (chatKeys.ObserveSample(key, down, focused, chatState, openKey, copyKey, chatScan)) {
                    CancelFromHook();
                }
            }
        }

        IntPtr OnMouse(int code, IntPtr message, IntPtr data) {
            int kind = message.ToInt32();
            try {
                if (code >= 0 && kind != 0x200) {
                    MouseData mouse = Marshal.PtrToStructure<MouseData>(data);
                    bool physical = (mouse.flags & 1) == 0;
                    var suppressMouse = SuppressPhysicalMouse;
                    if (physical && suppressMouse != null && suppressMouse(kind, new Point(mouse.x, mouse.y), mouse.data)) return new IntPtr(1);
                    if (physical && FocusStillMatches(wasFocused, sampledGameWindow, Native.GetForegroundWindow())) {
                        System.Threading.Interlocked.Increment(ref PhysicalInputRevision);
                        if (MouseActionBlocksCopy(kind, getCopyModifier != null ? getCopyModifier() : 0xA2)) {
                            lastMouseAction = DateTime.UtcNow;
                            Native.CancelCopyDelivery();
                        }
                        var onMouse = OnPhysicalMouse;
                        if (onMouse != null) onMouse(kind, new Point(mouse.x, mouse.y));
                    }
                }
            } catch (Exception) { /* Fail open: input must survive a monitoring failure. */ }
            return CallNextHookEx(mouseHook, code, message, data);
        }

        internal static bool MouseActionBlocksCopy(int message, int modifier) {
            // Wheel events cannot collide with a modifier that is no longer being injected.
            return (message != 0x20A && message != 0x20E) || modifier > 0;
        }

        internal bool ConsumesBinding(int key, int scanCode) {
            return (shouldDispatch == null || shouldDispatch(key, scanCode)) &&
                (sharedGameBinding == null || !sharedGameBinding(key, scanCode));
        }

        void CancelFromHook() {
            Native.CancelCopyDelivery();
            if (!Native.CopyInProgress || System.Threading.Interlocked.Exchange(ref cancelQueued, 1) != 0) return;
            if (uiDispatcher.HasShutdownStarted) { System.Threading.Interlocked.Exchange(ref cancelQueued, 0); return; }
            try { _ = uiDispatcher.BeginInvoke(cancelCopyAction); }
            catch (InvalidOperationException) { System.Threading.Interlocked.Exchange(ref cancelQueued, 0); }
        }

        IntPtr OnKey(int code, IntPtr message, IntPtr data) {
            try {
                if (code >= 0) {
                    KeyboardData key = Marshal.PtrToStructure<KeyboardData>(data);
                    int k = (int)key.key;
                    int eventScan = PhysicalKeyCapture.EncodeScanCode(key.scan, key.flags);
                    int msgCode = message.ToInt32();
                    bool isDown = msgCode == 0x100 || msgCode == 0x104;
                    bool injected = (key.flags & 0x10) != 0;
                    if (Native.ShouldSuppressCoordinateInput(injected, isDown, key.extra, false)) {
                        if (Native.InputTraceActive) Native.TraceInput("copy delivery blocked vk=" + k + " scan=" + eventScan + " chat=" + (chatState != null && chatState.Paused));
                        return new IntPtr(1);
                    }
                    bool traceKey = Native.InputTraceActive && key.extra != Native.AdminInputTag && !Native.IsCoordinateCopyInput(key.extra) &&
                        (k == 0x57 || k == 0x10 || k == 0xA0 || k == 0xA1 || k == 0x6F ||
                        (shouldDispatch != null && shouldDispatch(k, eventScan)) || (getCopyKey != null && k == getCopyKey()));
                    if (injected && (key.extra == Native.AdminInputTag || Native.IsCoordinateCopyInput(key.extra))) {
                        if (traceKey) Native.TraceInput("hook vk=" + k + " scan=" + eventScan + " flags=" + key.flags + " message=" + msgCode +
                            " source=internal decision=ignored-internal fresh=not-observed chat=" + (chatState != null && chatState.Paused) +
                            " inventory=not-queried modifiers=" + transitions.ShortcutModifiersDown(true) + " dispatch=False suppressed=False");
                        return CallNextHookEx(hook, code, message, data);
                    }
                    System.Threading.Interlocked.Increment(ref PhysicalInputRevision);
                    bool shortcutWasSuppressed = k >= 0 && k < suppressedShortcuts.Length && suppressedShortcuts[k];
                    if (shortcutWasSuppressed) {
                        if (!isDown) suppressedShortcuts[k] = false;
                        if (traceKey) Native.TraceInput("hook vk=" + k + " scan=" + eventScan + " flags=" + key.flags + " message=" + msgCode +
                            " source=physical decision=suppressed-held-edge fresh=not-observed chat=" + (chatState != null && chatState.Paused) +
                            " inventory=not-queried modifiers=" + transitions.ShortcutModifiersDown(true) + " dispatch=False suppressed=True");
                        return new IntPtr(1);
                    }
                    if (isDown) PhysicalKeyCapture.Observe(k, key.scan, key.flags);
                    IntPtr foreground = Native.GetForegroundWindow();
                    bool gameFocused = FocusStillMatches(wasFocused, sampledGameWindow, foreground);
                    int openKey = getChatOpenKey != null ? getChatOpenKey() : 0x54;
                    int copyKey = getCopyKey != null ? getCopyKey() : 0;
                    int chatScan = getChatScanCode != null ? getChatScanCode() : 0;
                    gameFocused = ShortcutEventFocused(gameFocused, (shouldDispatch != null && shouldDispatch(k, eventScan)) || PhysicalKeyCapture.Matches(k, eventScan, openKey, chatScan), () => Native.IsGameWindow(foreground));
                    bool chatWasPaused = chatState != null && chatState.Paused;
                    chatKeys.Observe(k, isDown, gameFocused, chatState, openKey, copyKey, eventScan, chatScan);
                    bool chatIsPaused = chatState != null && chatState.Paused;
                    bool fresh = transitions.Update(k, isDown);
                    bool altDown = transitions.IsDown(0x12) || transitions.IsDown(0xA4) || transitions.IsDown(0xA5)
                        || (Native.GetAsyncKeyState(0x12) & 0x8000) != 0 || (Native.GetAsyncKeyState(0xA4) & 0x8000) != 0 || (Native.GetAsyncKeyState(0xA5) & 0x8000) != 0;
                    if (Native.CopyInProgress && isDown && (k == 0x12 || k == 0xA4 || k == 0xA5 || k == 0x09 || k == 0x5B || k == 0x5C)) {
                        CancelFromHook();
                    }
                    if (k == 0x09 && altDown) {
                        Native.LastAltTabTime = DateTime.UtcNow;
                    }
                    if (gameFocused) {
                        DateTime now = DateTime.UtcNow;
                        LastUserInputTime = now;
                        if (fresh && isDown) {
                            LastFreshKeyDownTime = now;
                            CancelFromHook();
                        }
                    }
                    bool chatOwnsKey = ChatOwnsShortcut(chatWasPaused, chatIsPaused);
                    bool gameUiOwnsInput = gameFocused && inventoryOwnsInput != null && inventoryOwnsInput();
                    bool watched = shouldDispatch == null || shouldDispatch(k, eventScan);
                    bool chatControlKey = PhysicalKeyCapture.Matches(k, eventScan, openKey, chatScan) || k == 0xBF || k == 0x6F || k == 0x09 || k == 0x0D || k == 0x1B;
                    bool appShortcut = gameFocused && fresh && isDown && !chatOwnsKey && !gameUiOwnsInput && !chatControlKey && ConsumesBinding(k, eventScan)
                        && !(transitions.ShortcutModifiersDown(true) || altDown || (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0);
                    bool dispatched = fresh && isDown && !chatOwnsKey && !gameUiOwnsInput && !(k == 0x09 && altDown) && watched;
                    if (traceKey) Native.TraceInput("hook vk=" + k + " scan=" + eventScan + " flags=" + key.flags + " message=" + msgCode +
                        " source=" + (injected ? "external-injected" : "physical") + " fresh=" + fresh + " down=" + isDown + " focused=" + gameFocused + " watched=" + watched +
                        " chat_before=" + chatWasPaused + " chat_after=" + chatIsPaused + " chat_owns=" + chatOwnsKey +
                        " inventory=" + gameUiOwnsInput + " modifiers=" + transitions.ShortcutModifiersDown(true) + " alt=" + altDown +
                        " dispatch=" + dispatched + " suppressed=" + appShortcut + " copy_in_progress=" + Native.CopyInProgress);
                    if (dispatched) {
                        if (appShortcut && k >= 0 && k < suppressedShortcuts.Length) suppressedShortcuts[k] = true;
                        action(k, eventScan);
                    }
                    if (appShortcut) return new IntPtr(1);
                }
            } catch (Exception) { /* Fail open: never strand the user's physical input. */ }
            return CallNextHookEx(hook, code, message, data);
        }

        internal static bool ChatOwnsShortcut(bool wasPaused, bool isPaused) { return wasPaused || isPaused; }
        internal static bool ShortcutEventFocused(bool sampled, bool watched, Func<bool> verify) { return sampled || (watched && verify()); }
        internal static bool FocusStillMatches(bool focused, IntPtr sampledWindow, IntPtr foreground) {
            return focused && sampledWindow != IntPtr.Zero && sampledWindow == foreground;
        }

        public void Dispose() {
            disposed = true;
            Native.CancelActiveCopy();
            var kd = keyboardDispatch;
            var kc = keyboardContext;
            if (kd != null && !kd.IsDisposed && kc != null) {
                try { _ = kd.BeginInvoke(new Action(() => kc.ExitThread())); } catch (InvalidOperationException) { }
            }
            var md = mouseDispatch;
            var mc = mouseContext;
            if (md != null && !md.IsDisposed && mc != null) {
                try { _ = md.BeginInvoke(new Action(() => mc.ExitThread())); } catch (InvalidOperationException) { }
            }
        }
    }
}
