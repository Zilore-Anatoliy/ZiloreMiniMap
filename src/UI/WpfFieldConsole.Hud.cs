using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DrawingPoint=System.Drawing.PointF;

namespace ZiloreMiniMap {
    internal sealed partial class WpfFieldConsoleRuntime {
        Position smoothHudPos, cachedHudPosOut;
        bool smoothHudInterpolating;
        long lastSmoothTick;
        Position SmoothHudPosition(Position raw) {
            if(raw==null||double.IsNaN(raw.X)||double.IsNaN(raw.Y)||double.IsInfinity(raw.X)||double.IsInfinity(raw.Y)) { smoothHudPos=null; cachedHudPosOut=null; smoothHudInterpolating=false; return null; }
            double rz=(double.IsNaN(raw.Z)||double.IsInfinity(raw.Z))?(smoothHudPos==null?0:smoothHudPos.Z):raw.Z;
            double ry=(double.IsNaN(raw.Yaw)||double.IsInfinity(raw.Yaw))?(smoothHudPos==null?0:smoothHudPos.Yaw):raw.Yaw;
            long now=refreshClock.ElapsedMilliseconds;
            if(smoothHudPos==null) {
                smoothHudPos=new Position { X=raw.X,Y=raw.Y,Z=rz,Yaw=ry };
                cachedHudPosOut=new Position { X=raw.X,Y=raw.Y,Z=rz,Yaw=ry };
                smoothHudInterpolating=false;
                lastSmoothTick=now;
                return cachedHudPosOut;
            }
            double dt=Math.Max(0.001,Math.Min(0.25,(now-lastSmoothTick)/1000.0));
            lastSmoothTick=now;
            double dx=raw.X-smoothHudPos.X, dy=raw.Y-smoothHudPos.Y;
            double distSq=dx*dx+dy*dy;
            if(distSq>35000.0*35000.0) {
                smoothHudPos.X=raw.X;smoothHudPos.Y=raw.Y;smoothHudPos.Z=rz;smoothHudPos.Yaw=ry;
                cachedHudPosOut=new Position { X=raw.X,Y=raw.Y,Z=rz,Yaw=ry };
                smoothHudInterpolating=false;
                return cachedHudPosOut;
            }
            double dyaw=(ry-smoothHudPos.Yaw)%360.0;
            if(dyaw>180.0) dyaw-=360.0; else if(dyaw<-180.0) dyaw+=360.0;
            if(distSq<1.0 && Math.Abs(dyaw)<0.15 && Math.Abs(rz-smoothHudPos.Z)<5.0) {
                bool snapped=smoothHudInterpolating || cachedHudPosOut==null || smoothHudPos.X!=raw.X || smoothHudPos.Y!=raw.Y || smoothHudPos.Yaw!=ry;
                smoothHudPos.X=raw.X;smoothHudPos.Y=raw.Y;smoothHudPos.Z=rz;smoothHudPos.Yaw=ry;
                smoothHudInterpolating=false;
                if(snapped) cachedHudPosOut=new Position { X=raw.X,Y=raw.Y,Z=rz,Yaw=ry };
                return cachedHudPosOut;
            }
            double posAlpha=1.0-Math.Exp(-11.5*dt);
            double yawAlpha=1.0-Math.Exp(-14.0*dt);
            double ny=(smoothHudPos.Yaw+dyaw*yawAlpha)%360.0;
            if(ny>180.0) ny-=360.0; else if(ny<-180.0) ny+=360.0;
            smoothHudPos.X+=dx*posAlpha;
            smoothHudPos.Y+=dy*posAlpha;
            smoothHudPos.Z+=(rz-smoothHudPos.Z)*posAlpha;
            smoothHudPos.Yaw=ny;
            smoothHudInterpolating=true;
            cachedHudPosOut=new Position { X=smoothHudPos.X,Y=smoothHudPos.Y,Z=smoothHudPos.Z,Yaw=smoothHudPos.Yaw };
            return cachedHudPosOut;
        }

        internal static bool ShouldShowHud(bool enabled,bool tacticalVisible) { return enabled&&!tacticalVisible; }
        internal static System.Drawing.Rectangle RecoverHudBounds(System.Drawing.Rectangle desired,System.Drawing.Rectangle[] monitors) {
            if(monitors==null||monitors.Length==0)return desired;
            foreach(var monitor in monitors) { var visible=System.Drawing.Rectangle.Intersect(desired,monitor);if(visible.Width>=Math.Min(monitor.Width,desired.Width)&&visible.Height>=Math.Min(monitor.Height,desired.Height))return desired; }
            var nearest=monitors[0];double bestDist=double.MaxValue;
            for(int mi=0;mi<monitors.Length;mi++) {
                var m=monitors[mi];
                double dx=Math.Max(m.Left-desired.Left,Math.Max(0,(double)desired.Left-m.Right));
                double dy=Math.Max(m.Top-desired.Top,Math.Max(0,(double)desired.Top-m.Bottom));
                double d=dx*dx+dy*dy;
                if(d<bestDist) { bestDist=d; nearest=m; }
            }
            int w=Math.Min(desired.Width,Math.Max(120,nearest.Width));
            int h=Math.Min(desired.Height,Math.Max(120,nearest.Height));
            return new System.Drawing.Rectangle(Math.Max(nearest.Left,Math.Min(desired.Left,nearest.Right-w)),Math.Max(nearest.Top,Math.Min(desired.Top,nearest.Bottom-h)),w,h);
        }
        internal System.Drawing.Rectangle AdminMapBounds {
            get {
                if(!TacticalVisible)return System.Drawing.Rectangle.Empty;
                var origin=tacticalMap.PointToScreen(new Point(0,0));
                var end=tacticalMap.PointToScreen(new Point(tacticalMap.ActualWidth,tacticalMap.ActualHeight));
                return System.Drawing.Rectangle.FromLTRB((int)origin.X,(int)origin.Y,(int)end.X,(int)end.Y);
            }
        }
        bool TacticalVisible {
            get {
                if(!tactical.IsVisible||tactical.WindowState==WindowState.Minimized)return false;
                if(cachedTacticalHandle==IntPtr.Zero)cachedTacticalHandle=new WindowInteropHelper(tactical).Handle;
                return cachedTacticalHandle==IntPtr.Zero||!IsIconic(cachedTacticalHandle);
            }
        }
        void SyncHudVisibility(bool force=false) {
            if(disposed||exitRequested||hud==null||application.Dispatcher.HasShutdownStarted)return;
            bool show=ShouldShowHud(hudEnabled,TacticalVisible)&&!hudMoving;
            if(!show) { if(hud.IsVisible)hud.Hide();return; }
            if(!hud.IsLoaded&&new WindowInteropHelper(hud).Handle==IntPtr.Zero&&hudClosed)return;
            bool restore=!hud.IsVisible||hud.WindowState==WindowState.Minimized;
            if(hud.WindowState!=WindowState.Normal)hud.WindowState=WindowState.Normal;
            bool blockedByModal=dialogs.Count>0||(setupWindow!=null&&setupWindow.IsVisible&&setupHasFocus);
            if(!hud.IsVisible)hud.Show();
            if(hud.Topmost!=!blockedByModal) hud.Topmost=!blockedByModal;
            var handle=new WindowInteropHelper(hud).EnsureHandle();if(IsIconic(handle)) { ShowNativeWindow(handle,4);restore=true; }
            if(force||restore) { hudVisualHashValid=false;hudMap.InvalidateVisual(); }
            if(diagnostic)return;
            var hb=engine.HudBounds;double targetW=hb.Width,targetH=hb.Height+(engine.WpfShowStatus?HudStatusHeight:0);if(Math.Abs(hud.Width-targetW)>0.5)hud.Width=targetW;if(Math.Abs(hud.Height-targetH)>0.5)hud.Height=targetH;
            var source=HwndSource.FromHwnd(handle);if(source==null||source.CompositionTarget==null)return;
            var size=source.CompositionTarget.TransformToDevice.Transform(new Point(hud.Width,hud.Height));
            var bounds=RecoverHudBounds(new System.Drawing.Rectangle(hb.Left,hb.Top,Math.Max(1,(int)Math.Ceiling(size.X)),Math.Max(1,(int)Math.Ceiling(size.Y))),GetScreenWorkingAreas());
            if(bounds.X!=hb.Left||bounds.Y!=hb.Top)engine.WpfMoveHud(bounds.X,bounds.Y);
            bool raise=!blockedByModal&&(force||restore||refreshClock.ElapsedMilliseconds>=nextHudRaise);
            if(raise)nextHudRaise=refreshClock.ElapsedMilliseconds+1000;
            if(raise&&force) Native.EnsureTopmostNoActivate(handle,true);
            if(raise||restore||bounds!=lastHudBounds)SetWindowPos(handle,raise?new IntPtr(-1):IntPtr.Zero,bounds.X,bounds.Y,0,0,(uint)(0x0001|0x0010|0x0040|(raise?0:0x0004)));
            lastHudBounds=bounds;
        }
        internal void ToggleHud() {
            bool recover=!hudEnabled||!hud.IsVisible||hud.WindowState==WindowState.Minimized||IsIconic(new WindowInteropHelper(hud).Handle)||TacticalVisible;
            if(TacticalVisible) { tactical.Hide();tacticalMap.ClearHover();engine.WpfTactical(false); }
            hudEnabled=recover;SyncHudVisibility(true);
        }

        void SyncTacticalGlassAppearance() {
            string theme=WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme);
            if(cachedTacticalGlassTheme==theme)return;
            cachedTacticalGlassTheme=theme;
            bool isLight=string.Equals(theme,"Light",StringComparison.OrdinalIgnoreCase);
            bool isOled=string.Equals(theme,"OLED",StringComparison.OrdinalIgnoreCase);
            // Crisp Windows 11 Acrylic/Fluent glass for tactical map chrome (high readability over dark SCUM)
            Color bgCol=isLight?Color.FromArgb(236,246,249,252):(isOled?Color.FromArgb(232,5,8,13):Color.FromArgb(224,11,17,26));
            Color brdCol=isLight?Color.FromArgb(175,148,163,184):Color.FromArgb(120,56,189,248);
            Color dockCol=isLight?Color.FromArgb(228,248,250,252):(isOled?Color.FromArgb(224,6,10,16):Color.FromArgb(216,12,19,29));
            var glassBg=new SolidColorBrush(bgCol);glassBg.Freeze();
            var glassBrd=new SolidColorBrush(brdCol);glassBrd.Freeze();
            var dockBg=new SolidColorBrush(dockCol);dockBg.Freeze();
            if(tacticalSidebarShell!=null) { tacticalSidebarShell.Background=glassBg; tacticalSidebarShell.BorderBrush=glassBrd; }
            if(tacticalToolbarChrome!=null) { tacticalToolbarChrome.Background=dockBg; tacticalToolbarChrome.BorderBrush=glassBrd; }
            if(tacticalFloatingRailDock!=null) { tacticalFloatingRailDock.Background=dockBg; tacticalFloatingRailDock.BorderBrush=glassBrd; }
            if(targetCard!=null) { targetCard.Background=dockBg; targetCard.BorderBrush=glassBrd; }
            if(routePreviewCard!=null) { routePreviewCard.Background=dockBg; routePreviewCard.BorderBrush=glassBrd; }
            if(hoverCard!=null) { hoverCard.Background=dockBg; hoverCard.BorderBrush=glassBrd; }
            if(tacticalToastCard!=null) { tacticalToastCard.Background=dockBg; tacticalToastCard.BorderBrush=glassBrd; }
        }
        void SyncHudGlassAppearance(WpfMapState compact) {
            if(hudStatus==null||compact==null)return;
            string theme=WpfFieldTheme.ResolveTheme(WpfFieldTheme.ActiveTheme);
            string shape=compact.Shape??"Circle";
            int op=compact.Opacity;
            bool isCompactW=compact.HudWidth<210;
            if(cachedHudShape!=shape) {
                cachedHudShape=shape;
                bool isCircle=shape=="Circle";
                if(hudBezel!=null) hudBezel.Visibility=isCircle?Visibility.Visible:Visibility.Collapsed;
                if(hudInnerBezel!=null) hudInnerBezel.Visibility=isCircle?Visibility.Visible:Visibility.Collapsed;
                if(hudSquareBezel!=null) hudSquareBezel.Visibility=isCircle?Visibility.Collapsed:Visibility.Visible;
            }
            if(cachedHudCompactW!=isCompactW) {
                cachedHudCompactW=isCompactW;
                if(hudDividerBorder!=null) hudDividerBorder.Visibility=isCompactW?Visibility.Collapsed:Visibility.Visible;
                hudStatus.Padding=isCompactW?new Thickness(8,5,8,6):new Thickness(12,7,12,8);
            }
            if(cachedHudTheme==theme&&cachedHudOpacity==op)return;
            cachedHudTheme=theme;cachedHudOpacity=op;
            // Map HUD opacity (30..100) to airy Windows 11 glass alpha (0.38..0.74) so the game always shows through.
            bool isLight=string.Equals(theme,"Light",StringComparison.OrdinalIgnoreCase);
            bool isOled=string.Equals(theme,"OLED",StringComparison.OrdinalIgnoreCase);
            double normOp=Math.Max(0.0,Math.Min(1.0,(op-30.0)/70.0));
            byte alpha=(byte)Math.Round(255.0*(isLight?(0.74+0.20*normOp):(0.52+0.34*normOp)));
            Color bgCol=isLight?Color.FromArgb(alpha,248,250,252):(isOled?Color.FromArgb(alpha,4,6,10):Color.FromArgb(alpha,11,17,26));
            Color brdCol=isLight?Color.FromArgb(175,2,132,199):Color.FromArgb(135,56,189,248);
            var bgBrush=new SolidColorBrush(bgCol);bgBrush.Freeze();
            var brdBrush=new SolidColorBrush(brdCol);brdBrush.Freeze();
            hudStatus.Background=bgBrush;
            hudStatus.BorderBrush=brdBrush;
            if(hudLocation!=null) hudLocation.Foreground=isLight?WpfFieldTheme.ColorBrush("#0F172A"):WpfFieldTheme.ColorBrush("#F8FAFC");
            if(hudRealClock!=null) hudRealClock.Foreground=isLight?WpfFieldTheme.ColorBrush("#334155"):WpfFieldTheme.ColorBrush("#CBD5E1");
            if(hudTelemetry!=null) hudTelemetry.Foreground=isLight?WpfFieldTheme.ColorBrush("#1E293B"):WpfFieldTheme.ColorBrush("#CBD5E1");
            if(hudDividerBorder!=null) hudDividerBorder.Background=isLight?WpfFieldTheme.ColorBrush("#3364748B"):WpfFieldTheme.ColorBrush("#3894A3B8");
            if(hudSectorBadge!=null) { hudSectorBadge.Background=WpfFieldTheme.ColorBrush(isLight?"#220284C7":"#3338BDF8"); hudSectorBadge.BorderBrush=WpfFieldTheme.ColorBrush(isLight?"#880284C7":"#7038BDF8"); }
        }
    }
}
