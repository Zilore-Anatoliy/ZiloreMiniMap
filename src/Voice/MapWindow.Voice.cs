#nullable enable
using System;

namespace ZiloreMiniMap {
    public sealed partial class MapWindow {
        IVoiceService? voiceService;
        IVoiceService Voice {
            get {
                if (voiceService == null) voiceService = new VoiceNavigationService(dataFolder);
                return voiceService;
            }
        }

        // Compatibility bridges for settings/lifecycle/death-banner modules
        bool voiceEnabled { get { return Voice.Enabled; } set { Voice.Enabled = value; } }
        string voiceName { get { return Voice.VoiceName; } set { Voice.VoiceName = value; } }
        int voiceVolume { get { return Voice.Volume; } set { Voice.Volume = value; } }
        int voiceOutputDevice { get { return Voice.OutputDevice; } set { Voice.OutputDevice = value; } }
        VoiceNavigator voiceNavigator { get { return Voice.Navigator; } }
        MapZone? voiceTarget { get { return Voice.ActiveTarget; } set { Voice.ActiveTarget = value; } }
        DateTime voiceStatusUntil { get { return Voice.StatusUntilUtc; } set { Voice.StatusUntilUtc = value; } }
        DateTime nextVoiceUpdate { get { return Voice.NextUpdateUtc; } set { Voice.NextUpdateUtc = value; } }
        Position? lastVoicePosition { get { return Voice.LastPosition; } set { Voice.LastPosition = value; } }
        RoadRoute? lastVoiceRoute { get { return Voice.LastRoute; } set { Voice.LastRoute = value; } }
        IDisposable? voicePlayer { get { return voiceService; } }

        string VoiceStatus { get { return Voice.Status; } }
        string VoiceRoot { get { return Voice.VoiceRoot; } }
        static string RouteUnavailableText { get { return Localization.Get("VoiceUnavailable"); } }

        void StopVoice() { Voice.Stop(); }

        void FinishVoiceReroute(bool success) {
            Voice.FinishReroute(success);
            lastFrameKey = null;
        }

        void VoiceArrived() {
            Voice.NotifyArrived(!diagnosticMode && Native.GameFocused() && !chat.Paused);
        }

        void TickVoice(DateTime now, bool gameFocused) {
            double maxAgeSec = Math.Max(5.0, (Program.TrackingIntervalMs(copyIntervalMs, scumCopyModifierKey) * 2.5) / 1000.0);
            Voice.Tick(now, gameFocused, chat.Paused, SettingsVisible, position, updated, searchTarget, activeRoute,
                (pt, target) => RequestRouteAsync(pt, target, true), maxAgeSec);
        }
    }
}
