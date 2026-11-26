using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZiloreMiniMap {
    // Wheel handlers only record intent. A single UI timer applies the combined
    // movement, so a fast wheel cannot schedule a render/rebuild for every notch.
    internal sealed class BatchedWheel:IDisposable {
        readonly Timer timer;
        readonly Action<int> apply;
        int pending;
        bool disposed;
        internal bool Waiting { get { return !disposed && timer.Enabled; } }
        internal BatchedWheel(Action<int> apply) {
            this.apply=apply;timer=new Timer { Interval=50 };
            timer.Tick+=(s,e)=>Flush();
        }
        internal void Add(int delta) {
            if(disposed)return;
            pending=(int)Math.Max(-120L*256,Math.Min(120L*256,(long)pending+delta));
            if(!timer.Enabled)timer.Start();
        }
        internal void Flush() {
            timer.Stop();if(disposed)return;
            int steps=pending/120;pending%=120;
            if(steps!=0)apply(steps);
        }
        internal void Cancel() { timer.Stop();pending=0; }
        public void Dispose() { if(disposed)return;disposed=true;timer.Dispose();pending=0; }
    }
    internal static class FullMapSidebarStyle {
        internal static readonly Color Background=Color.FromArgb(14,17,20);
        internal static readonly Color Border=OverlayTheme.Border;
        internal static readonly Color Divider=Color.FromArgb(60,60,75,90);
        internal static readonly Color ButtonSurface=OverlayTheme.Surface;
        internal static readonly Color SelectedSurface=OverlayTheme.SurfaceCard;
        internal static readonly Color Muted=Color.FromArgb(160,170,180);
        internal static readonly Color Secondary=Color.FromArgb(120,135,150);
        internal static readonly Color Ink=Color.WhiteSmoke;
        internal static readonly Color MiniInk=Color.FromArgb(180,190,200);
        internal static readonly Color SelectedInk=OverlayTheme.Accent;
        internal static readonly Color Accent=OverlayTheme.Accent;
        internal static readonly Color Arrow=OverlayTheme.InkMuted;
        internal static readonly Color SectionExpanded=Color.FromArgb(200,22,27,32);
        internal static readonly Color SectionCollapsed=Color.FromArgb(160,16,20,24);
        internal static readonly Color SectionBorder=Color.FromArgb(140,42,52,61);
        internal static readonly Color ScrollTrack=Color.FromArgb(40,255,255,255);
        internal static readonly Color ScrollThumb=Color.FromArgb(160,70,140,200);
        internal static readonly Color ScrollThumbDragging=Color.FromArgb(220,80,170,240);

        static GraphicsPath RoundedPath(Rectangle rect,int diameter) {
            var path=new GraphicsPath();
            int arc=Math.Min(diameter,Math.Min(rect.Width,rect.Height));
            if(arc<=0)return path;
            path.AddArc(rect.X,rect.Y,arc,arc,180,90);
            path.AddArc(rect.Right-arc,rect.Y,arc,arc,270,90);
            path.AddArc(rect.Right-arc,rect.Bottom-arc,arc,arc,0,90);
            path.AddArc(rect.X,rect.Bottom-arc,arc,arc,90,90);
            path.CloseFigure();return path;
        }
        internal static void DrawMiniButton(Graphics graphics,Rectangle rect,string text,bool selected) {
            using(var font=SystemTypography.CreateOwnedGdiFont(7f,FontStyle.Bold))DrawMiniButton(graphics,rect,text,selected,font,true,false);
        }
        internal static void DrawMiniButton(Graphics graphics,Rectangle rect,string text,bool selected,Font font,bool enabled,bool compact=true) {
            if(rect.Width<=0 || rect.Height<=0)return;
            using(var path=RoundedPath(rect,6)) {
                using(var fill=new SolidBrush(selected && enabled?SelectedSurface:ButtonSurface))graphics.FillPath(fill,path);
                using(var border=new Pen(selected && enabled?Accent:Border,1f))graphics.DrawPath(border,path);
            }
            DrawText(graphics,rect,text,font,!enabled?Secondary:(selected?SelectedInk:MiniInk),compact);
        }
        internal static void DrawActionButton(Graphics graphics,Rectangle rect,string text,Color background) {
            using(var font=SystemTypography.CreateOwnedGdiFont(7.5f,FontStyle.Bold))DrawActionButton(graphics,rect,text,background,font,true,false);
        }
        internal static void DrawActionButton(Graphics graphics,Rectangle rect,string text,Color background,Font font,bool enabled,bool compact=true) {
            if(rect.Width<=0 || rect.Height<=0)return;
            using(var path=RoundedPath(rect,7)) {
                using(var fill=new SolidBrush(background))graphics.FillPath(fill,path);
                using(var border=new Pen(OverlayTheme.Border,1f))graphics.DrawPath(border,path);
            }
            DrawText(graphics,rect,text,font,enabled?Ink:Secondary,compact);
        }
        static void DrawText(Graphics graphics,Rectangle rect,string text,Font font,Color ink,bool compact) {
            SystemTypography.ApplyGdiClearType(graphics);
            using(var brush=new SolidBrush(ink))
            using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,
                Trimming=compact?StringTrimming.EllipsisCharacter:StringTrimming.None,FormatFlags=compact?StringFormatFlags.NoWrap:0 })
                graphics.DrawString(text??"",font,brush,rect,format);
        }
    }

    internal sealed class SidebarButton:Button {
        readonly Font sidebarFont=SystemTypography.GetGdiFont(7.5f,FontStyle.Bold);
        bool selected,sectionHeader,expanded;
        internal bool Pill;
        internal bool Selected { get { return selected; } set { if(selected!=value) { selected=value;Invalidate(); } } }
        internal bool SectionHeader { get { return sectionHeader; } set { if(sectionHeader!=value) { sectionHeader=value;Invalidate(); } } }
        internal bool Expanded { get { return expanded; } set { if(expanded!=value) { expanded=value;Invalidate(); } } }
        internal SidebarButton() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;BackColor=FullMapSidebarStyle.Background;
            ForeColor=FullMapSidebarStyle.Ink;Font=sidebarFont;Cursor=Cursors.Hand;AutoEllipsis=true;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var rect=new Rectangle(0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
            if(sectionHeader) {
                using(var fill=new SolidBrush(expanded?FullMapSidebarStyle.SectionExpanded:FullMapSidebarStyle.SectionCollapsed))e.Graphics.FillRectangle(fill,rect);
                using(var border=new Pen(FullMapSidebarStyle.SectionBorder,1f))e.Graphics.DrawRectangle(border,rect);
                using(var arrowFont=SystemTypography.CreateOwnedGdiFont(7f,FontStyle.Bold))
                using(var arrow=new SolidBrush(Enabled?FullMapSidebarStyle.Arrow:FullMapSidebarStyle.Secondary))
                    e.Graphics.DrawString(expanded?"\u25bc":"\u25ba",arrowFont,arrow,6,6);
                using(var textBrush=new SolidBrush(Enabled?FullMapSidebarStyle.Ink:FullMapSidebarStyle.Secondary))
                using(var format=new StringFormat { LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,
                    FormatFlags=StringFormatFlags.NoWrap })
                    e.Graphics.DrawString(Text,Font,textBrush,new Rectangle(22,0,Math.Max(0,Width-26),Height),format);
            } else if(Pill && rect.Width>rect.Height && rect.Height>0) {
                int diameter=rect.Height;
                using(var shape=new GraphicsPath()) {
                    shape.AddArc(rect.Left,rect.Top,diameter,diameter,90,180);
                    shape.AddArc(rect.Right-diameter,rect.Top,diameter,diameter,270,180);shape.CloseFigure();
                    using(var fill=new SolidBrush(selected?FullMapSidebarStyle.SelectedSurface:FullMapSidebarStyle.ButtonSurface))e.Graphics.FillPath(fill,shape);
                    using(var border=new Pen(selected?OverlayTheme.Accent:FullMapSidebarStyle.Border))e.Graphics.DrawPath(border,shape);
                }
                TextRenderer.DrawText(e.Graphics,Text,Font,rect,Enabled?(selected?OverlayTheme.Accent:FullMapSidebarStyle.Ink):FullMapSidebarStyle.Secondary,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            } else if(Height<=22 || selected)FullMapSidebarStyle.DrawMiniButton(e.Graphics,rect,Text,selected,Font,Enabled);
            else FullMapSidebarStyle.DrawActionButton(e.Graphics,rect,Text,OverlayTheme.Surface,Font,Enabled);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(rect,-4,-4),FullMapSidebarStyle.Muted,BackColor);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e);Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e);Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }

    internal sealed class SidebarRadioButton:RadioButton {
        readonly Font sidebarFont=SystemTypography.GetGdiFont(7f,FontStyle.Bold);
        internal SidebarRadioButton() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Appearance=Appearance.Button;FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;
            BackColor=FullMapSidebarStyle.Background;Font=sidebarFont;Cursor=Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var rect=new Rectangle(0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
            FullMapSidebarStyle.DrawMiniButton(e.Graphics,rect,Text,Checked,Font,Enabled);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(rect,-4,-4),FullMapSidebarStyle.Muted,BackColor);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e);Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e);Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }

    internal sealed class SidebarSection:GroupBox {
        internal SidebarSection() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=FullMapSidebarStyle.Background;ForeColor=FullMapSidebarStyle.Muted;
        }
        public override Rectangle DisplayRectangle {
            get { return new Rectangle(Padding.Left,Padding.Top,Math.Max(0,ClientSize.Width-Padding.Horizontal),Math.Max(0,ClientSize.Height-Padding.Vertical)); }
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor);
            if(!string.IsNullOrEmpty(Text))TextRenderer.DrawText(e.Graphics,Text,Font,
                new Rectangle(Padding.Left,0,Math.Max(0,Width-Padding.Horizontal),Font.Height+2),ForeColor,
                TextFormatFlags.Left|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class SidebarScrollPanel:Panel {
        const int BarWidth=4,BarInset=2;
        readonly Panel viewport;
        readonly System.Collections.Generic.HashSet<Control> hooked=new System.Collections.Generic.HashSet<Control>();
        internal Panel Content { get; private set; }
        int minimumContentWidth=240,maximumScrollX,maximumScrollY;
        readonly BatchedWheel wheel;
        int contentUpdateDepth;
        internal event EventHandler ViewportChanged;
        internal Action<int> WheelNavigation;
        internal Rectangle VisibleContentBounds { get { return new Rectangle(scrollOffset,viewport.ClientSize); } }
        internal void BeginContentUpdate() { contentUpdateDepth++; }
        internal void EndContentUpdate() { if(contentUpdateDepth>0 && --contentUpdateDepth==0)RefreshContentLayout(); }
        Point scrollOffset;
        bool layingOut,draggingHorizontal,draggingVertical;
        Point dragStart,dragOffset;
        Rectangle verticalTrack,verticalThumb,horizontalTrack,horizontalThumb;
        internal int MinimumContentWidth { get { return minimumContentWidth; } set { minimumContentWidth=Math.Max(0,value);RefreshContentLayout(); } }
        internal int MaximumScrollX { get { return maximumScrollX; } }
        internal int MaximumScrollY { get { return maximumScrollY; } }
        internal Point ScrollOffset { get { return scrollOffset; } set { SetScrollOffset(value); } }
        internal SidebarScrollPanel() {
            wheel=new BatchedWheel(steps=> {
                if(IsDisposed || Disposing)return;
                if(WheelNavigation!=null)WheelNavigation(-steps);
                else if(maximumScrollX>0 && (((ModifierKeys&Keys.Shift)!=0) || maximumScrollY==0))ScrollBy(-steps*48,0);
                else ScrollBy(0,-steps*48);
            });
            VisibleChanged+=(s,e)=> { if(!Visible)wheel.Cancel(); };
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Selectable,false);TabStop=false;AutoScroll=false;
            BackColor=FullMapSidebarStyle.Background;Padding=new Padding(10,0,10,0);
            viewport=new Panel { BackColor=FullMapSidebarStyle.Background,TabStop=false };
            Content=new Panel { BackColor=FullMapSidebarStyle.Background,TabStop=false };
            viewport.Controls.Add(Content);Controls.Add(viewport);
            Content.ControlAdded+=(s,e)=> { Hook(e.Control);RefreshContentLayout(); };
            Content.ControlRemoved+=(s,e)=> { Unhook(e.Control);RefreshContentLayout(); };
            Content.Layout+=(s,e)=>RefreshContentLayout();
            viewport.MouseWheel+=ChildWheel;Content.MouseWheel+=ChildWheel;
        }
        void Hook(Control control) {
            if(!hooked.Add(control))return;
            control.SizeChanged+=ChildLayoutChanged;control.VisibleChanged+=ChildLayoutChanged;control.DockChanged+=ChildLayoutChanged;
            control.GotFocus+=ChildGotFocus;control.ControlAdded+=DescendantAdded;control.ControlRemoved+=DescendantRemoved;
            if(!(control is NumericUpDown) && !InNumericInput(control))control.MouseWheel+=ChildWheel;
            foreach(Control child in control.Controls)Hook(child);
        }
        void Unhook(Control control) {
            if(!hooked.Remove(control))return;
            control.SizeChanged-=ChildLayoutChanged;control.VisibleChanged-=ChildLayoutChanged;control.DockChanged-=ChildLayoutChanged;
            control.GotFocus-=ChildGotFocus;control.ControlAdded-=DescendantAdded;control.ControlRemoved-=DescendantRemoved;control.MouseWheel-=ChildWheel;
            foreach(Control child in control.Controls)Unhook(child);
        }
        static bool InNumericInput(Control control) {
            for(Control parent=control.Parent;parent!=null;parent=parent.Parent)if(parent is NumericUpDown)return true;
            return false;
        }
        void DescendantAdded(object sender,ControlEventArgs e) { Hook(e.Control);RefreshContentLayout(); }
        void DescendantRemoved(object sender,ControlEventArgs e) { Unhook(e.Control);RefreshContentLayout(); }
        void ChildLayoutChanged(object sender,EventArgs e) { if(!layingOut)RefreshContentLayout(); }
        void ChildGotFocus(object sender,EventArgs e) { ScrollControlIntoView((Control)sender); }
        void ChildWheel(object sender,MouseEventArgs e) { HandleWheel(e); }
        void HandleWheel(MouseEventArgs e) {
            var handled=e as HandledMouseEventArgs;if(handled!=null && handled.Handled)return;
            wheel.Add(e.Delta);
            if(handled!=null)handled.Handled=true;
        }
        protected override void Dispose(bool disposing) { if(disposing)wheel.Dispose();base.Dispose(disposing); }
        internal void RefreshContentLayout() {
            if(contentUpdateDepth>0 || layingOut || viewport==null || Content==null || IsDisposed || Disposing)return;
            Rectangle previousViewport=VisibleContentBounds;
            layingOut=true;
            try {
                int contentHeight=0;
                foreach(Control control in Content.Controls)if(control.Visible && control.Dock==DockStyle.Top)contentHeight+=Math.Max(0,control.Height);
                bool horizontal=false,vertical=false;
                int viewWidth=0,viewHeight=0,contentWidth=0;
                for(int pass=0;pass<3;pass++) {
                    int extraRight=vertical?Math.Max(0,BarWidth+BarInset-Padding.Right):0;
                    int extraBottom=horizontal?Math.Max(0,BarWidth+BarInset-Padding.Bottom):0;
                    viewWidth=Math.Max(0,ClientSize.Width-Padding.Horizontal-extraRight);
                    viewHeight=Math.Max(0,ClientSize.Height-Padding.Vertical-extraBottom);
                    contentWidth=Math.Max(minimumContentWidth,viewWidth);
                    horizontal=contentWidth>viewWidth;vertical=contentHeight>viewHeight;
                }
                viewport.Bounds=new Rectangle(Padding.Left,Padding.Top,viewWidth,viewHeight);
                Content.SuspendLayout();
                try {
                    Content.Size=new Size(contentWidth,Math.Max(contentHeight,viewHeight));
                    int y=0;
                    for(int index=Content.Controls.Count-1;index>=0;index--) {
                        Control control=Content.Controls[index];
                        if(!control.Visible || control.Dock!=DockStyle.Top)continue;
                        control.SetBounds(0,y,contentWidth,control.Height);y+=control.Height;
                    }
                } finally { Content.ResumeLayout(true); }
                maximumScrollX=Math.Max(0,contentWidth-viewWidth);maximumScrollY=Math.Max(0,contentHeight-viewHeight);
                SetScrollOffset(scrollOffset);UpdateScrollbars();Invalidate();
            } finally { layingOut=false; }
            if(previousViewport!=VisibleContentBounds && ViewportChanged!=null)ViewportChanged(this,EventArgs.Empty);
        }
        void SetScrollOffset(Point value) {
            Point previous=scrollOffset;
            scrollOffset=new Point(Math.Max(0,Math.Min(maximumScrollX,value.X)),Math.Max(0,Math.Min(maximumScrollY,value.Y)));
            if(Content!=null)Content.Location=new Point(-scrollOffset.X,-scrollOffset.Y);
            UpdateScrollbars();Invalidate();
            if(previous!=scrollOffset && !layingOut && ViewportChanged!=null)ViewportChanged(this,EventArgs.Empty);
        }
        internal void ScrollBy(int dx,int dy) {
            int x=(int)Math.Max(0L,Math.Min((long)maximumScrollX,(long)scrollOffset.X+dx));
            int y=(int)Math.Max(0L,Math.Min((long)maximumScrollY,(long)scrollOffset.Y+dy));
            SetScrollOffset(new Point(x,y));
        }
        internal new void ScrollControlIntoView(Control control) {
            if(control==null || viewport==null || Content==null)return;
            Point position=Point.Empty;Control current=control;
            while(current!=null && current!=Content) { position.Offset(current.Left,current.Top);current=current.Parent; }
            if(current!=Content)return;
            int x=scrollOffset.X,y=scrollOffset.Y;
            if(control.Width>viewport.Width || position.X<x)x=position.X;
            else if(position.X+control.Width>x+viewport.Width)x=position.X+control.Width-viewport.Width;
            if(control.Height>viewport.Height || position.Y<y)y=position.Y;
            else if(position.Y+control.Height>y+viewport.Height)y=position.Y+control.Height-viewport.Height;
            SetScrollOffset(new Point(x,y));
        }
        void UpdateScrollbars() {
            if(viewport==null || Content==null)return;
            verticalTrack=maximumScrollY>0?new Rectangle(Math.Max(0,ClientSize.Width-BarWidth-BarInset),viewport.Top,BarWidth,viewport.Height):Rectangle.Empty;
            horizontalTrack=maximumScrollX>0?new Rectangle(viewport.Left,Math.Max(0,ClientSize.Height-BarWidth-BarInset),viewport.Width,BarWidth):Rectangle.Empty;
            verticalThumb=Thumb(verticalTrack,viewport.Height,Content.Height,scrollOffset.Y,maximumScrollY,true);
            horizontalThumb=Thumb(horizontalTrack,viewport.Width,Content.Width,scrollOffset.X,maximumScrollX,false);
        }
        static Rectangle Thumb(Rectangle track,int visible,int total,int offset,int maximum,bool vertical) {
            int length=vertical?track.Height:track.Width;
            if(length<=0 || maximum<=0)return Rectangle.Empty;
            int size=Math.Min(length,Math.Max(24,(int)((long)visible*length/Math.Max(1,total))));
            int start=(int)((long)offset*(length-size)/maximum);
            return vertical?new Rectangle(track.X,track.Y+start,track.Width,size):new Rectangle(track.X+start,track.Y,size,track.Height);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);UpdateScrollbars();
            using(var track=new SolidBrush(FullMapSidebarStyle.ScrollTrack)) {
                if(!verticalTrack.IsEmpty)e.Graphics.FillRectangle(track,verticalTrack);
                if(!horizontalTrack.IsEmpty)e.Graphics.FillRectangle(track,horizontalTrack);
            }
            using(var thumb=new SolidBrush(draggingVertical || draggingHorizontal?FullMapSidebarStyle.ScrollThumbDragging:FullMapSidebarStyle.ScrollThumb)) {
                if(!verticalThumb.IsEmpty)e.Graphics.FillRectangle(thumb,verticalThumb);
                if(!horizontalThumb.IsEmpty)e.Graphics.FillRectangle(thumb,horizontalThumb);
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { HandleWheel(e);base.OnMouseWheel(e); }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;UpdateScrollbars();
            if(maximumScrollY>0 && Rectangle.Inflate(verticalTrack,4,0).Contains(e.Location)) {
                if(!verticalThumb.Contains(e.Location)) {
                    int travel=verticalTrack.Height-verticalThumb.Height;
                    int value=travel<=0?0:(int)((long)(e.Y-verticalTrack.Top-verticalThumb.Height/2)*maximumScrollY/travel);
                    SetScrollOffset(new Point(scrollOffset.X,value));
                }
                draggingVertical=true;
            } else if(maximumScrollX>0 && Rectangle.Inflate(horizontalTrack,0,4).Contains(e.Location)) {
                if(!horizontalThumb.Contains(e.Location)) {
                    int travel=horizontalTrack.Width-horizontalThumb.Width;
                    int value=travel<=0?0:(int)((long)(e.X-horizontalTrack.Left-horizontalThumb.Width/2)*maximumScrollX/travel);
                    SetScrollOffset(new Point(value,scrollOffset.Y));
                }
                draggingHorizontal=true;
            }
            if(draggingVertical || draggingHorizontal) { dragStart=e.Location;dragOffset=scrollOffset;Capture=true;Invalidate(); }
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if(draggingVertical) {
                int travel=verticalTrack.Height-verticalThumb.Height;
                int change=travel<=0?0:(int)((long)(e.Y-dragStart.Y)*maximumScrollY/travel);
                SetScrollOffset(new Point(scrollOffset.X,dragOffset.Y+change));
            } else if(draggingHorizontal) {
                int travel=horizontalTrack.Width-horizontalThumb.Width;
                int change=travel<=0?0:(int)((long)(e.X-dragStart.X)*maximumScrollX/travel);
                SetScrollOffset(new Point(dragOffset.X+change,scrollOffset.Y));
            }
        }
        void CancelDragging() { draggingHorizontal=draggingVertical=false;if(Capture)Capture=false;Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e);if(e.Button==MouseButtons.Left)CancelDragging(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e);if(!Capture)CancelDragging(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e);RefreshContentLayout(); }
        protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e);RefreshContentLayout(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e);if(!Visible)CancelDragging();RefreshContentLayout(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e);if(!Enabled)CancelDragging(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e);if(Parent==null)CancelDragging(); }
    }

    // Shared visual language for interactive HUD panels and popup menus (Tactical Survival Hardware aesthetic).
    public static partial class OverlayTheme {
        public static Color Background { get; private set; } = Color.FromArgb(14,17,20);
        public static Color Surface { get; private set; } = Color.FromArgb(21,26,31);
        public static Color SurfaceCard { get; private set; } = Color.FromArgb(29,36,43);
        public static Color Accent { get; private set; } = Color.FromArgb(255,181,71);
        public static Color AccentHover { get; private set; } = Color.FromArgb(255,199,115);
        public static Color Border { get; private set; } = Color.FromArgb(42,51,60);
        public static Color BorderHighlight { get; private set; } = Color.FromArgb(126,143,159);
        public static Color Ink { get; private set; } = Color.FromArgb(233,238,242);
        public static Color InkMuted { get; private set; } = Color.FromArgb(164,177,188);
        public static Color Route { get; private set; } = Color.FromArgb(53,214,200);
        public static Color Alert { get; private set; } = Color.FromArgb(255,107,94);

        internal static void SyncWithWpfTheme(string resolvedTheme) {
            if(string.Equals(resolvedTheme,"Light",StringComparison.OrdinalIgnoreCase)) {
                Background=Color.FromArgb(248,250,252);
                Surface=Color.FromArgb(226,232,240);
                SurfaceCard=Color.FromArgb(241,245,249);
                Accent=Color.FromArgb(217,119,6);
                AccentHover=Color.FromArgb(180,83,9);
                Border=Color.FromArgb(203,213,225);
                BorderHighlight=Color.FromArgb(148,163,184);
                Ink=Color.FromArgb(15,23,42);
                InkMuted=Color.FromArgb(71,85,105);
                Route=Color.FromArgb(13,148,136);
            } else if(string.Equals(resolvedTheme,"OLED",StringComparison.OrdinalIgnoreCase)) {
                Background=Color.FromArgb(0,0,0);
                Surface=Color.FromArgb(14,18,23);
                SurfaceCard=Color.FromArgb(20,26,33);
                Accent=Color.FromArgb(255,181,71);
                AccentHover=Color.FromArgb(255,199,115);
                Border=Color.FromArgb(29,37,46);
                BorderHighlight=Color.FromArgb(100,116,139);
                Ink=Color.FromArgb(245,248,252);
                InkMuted=Color.FromArgb(138,153,168);
                Route=Color.FromArgb(45,212,191);
            } else {
                Background=Color.FromArgb(14,17,20);
                Surface=Color.FromArgb(21,26,31);
                SurfaceCard=Color.FromArgb(29,36,43);
                Accent=Color.FromArgb(255,181,71);
                AccentHover=Color.FromArgb(255,199,115);
                Border=Color.FromArgb(42,51,60);
                BorderHighlight=Color.FromArgb(126,143,159);
                Ink=Color.FromArgb(233,238,242);
                InkMuted=Color.FromArgb(164,177,188);
                Route=Color.FromArgb(53,214,200);
            }
        }
        // Native Windows 11/10 Segoe UI Variable (ClearType) font cache.
        internal static Font UiFont(float size,FontStyle style=FontStyle.Regular) {
            return SystemTypography.GetGdiFont(size,style);
        }

        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ReleaseCapture();
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] private static partial IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);

        public static void Style(Control root) {
            if(root.ForeColor!=InkMuted && root.ForeColor!=Accent && root.ForeColor!=Route)root.ForeColor=Ink;
            Button button=root as Button;
            if(button!=null) {
                if((string)button.Tag == "swatch") return;
                button.FlatStyle=FlatStyle.Flat;
                button.FlatAppearance.BorderSize=1;
                button.FlatAppearance.BorderColor=Border;
                button.FlatAppearance.MouseOverBackColor=SurfaceCard;
                button.FlatAppearance.MouseDownBackColor=SurfaceCard;
                button.BackColor=Surface;
                button.UseVisualStyleBackColor=false;
                button.Cursor=Cursors.Hand;
                button.Height=Math.Max(28,button.Height);
            } else if(root is TextBox || root is ListBox || root is NumericUpDown) {
                root.BackColor=Color.FromArgb(14,17,20);
                if(root.ForeColor!=InkMuted && root.ForeColor!=Accent && root.ForeColor!=Route)root.ForeColor=Ink;
            } else if(root is TreeView) {
                TreeView tv=root as TreeView;
                tv.BackColor=Color.FromArgb(14,17,20);
                tv.ForeColor=Ink;
                tv.LineColor=Border;
            } else if(!(root is Panel) || (root.BackColor!=Surface && root.BackColor!=SurfaceCard)) {
                root.BackColor=Background;
            }
            CheckBox check=root as CheckBox;
            if(check!=null) {
                check.FlatStyle=FlatStyle.Flat;
                check.Cursor=Cursors.Hand;
                check.ForeColor=Ink;
                check.FlatAppearance.BorderSize=1;
                check.FlatAppearance.BorderColor=Border;
                check.FlatAppearance.CheckedBackColor=Accent;
                check.FlatAppearance.MouseDownBackColor=SurfaceCard;
                check.FlatAppearance.MouseOverBackColor=SurfaceCard;
            }
            ComboBox combo=root as ComboBox;
            if(combo!=null) {
                combo.FlatStyle=FlatStyle.Flat;
                combo.BackColor=Surface;
                combo.ForeColor=Ink;
                combo.DrawMode=DrawMode.OwnerDrawFixed;
                combo.ItemHeight=22;
                if((string)combo.Tag != "styled") {
                    combo.Tag = "styled";
                    combo.DrawItem+=(s,e)=> {
                        if(e.Index<0) return;
                        bool sel=(e.State & DrawItemState.Selected)!=0;
                        using(Brush b=new SolidBrush(sel?SurfaceCard:Surface)) e.Graphics.FillRectangle(b,e.Bounds);
                        string itmText=combo.Items[e.Index].ToString();
                        TextRenderer.DrawText(e.Graphics, itmText, combo.Font, e.Bounds, sel?Accent:Ink, TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
                    };
                }
            }
            if(!(root is NumericUpDown)) {
                foreach(Control child in root.Controls) Style(child);
            }
        }

        public static void Frame(Form form,string title,Action dismiss,int headerHeight=52) {
            form.FormBorderStyle=FormBorderStyle.None; form.TopMost=true; form.ShowInTaskbar=true;
            form.Padding=new Padding(1); form.BackColor=Background; form.Font=SystemTypography.GetGdiFont(9f);
            Panel header=new Panel { Dock=DockStyle.Top,Height=headerHeight,Padding=new Padding(14,8,8,8),BackColor=Surface };
            Label caption=new Label { Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=SystemTypography.GetGdiFont(11f,FontStyle.Bold),ForeColor=Accent };
            Button close=new Button { Name="CloseWindow",Text=Localization.Get("Close"),Dock=DockStyle.Right,Width=62,TabStop=false,CausesValidation=false };
            close.Click+=(s,e)=>dismiss();
            caption.MouseDown+=(s,e)=> { if(e.Button==MouseButtons.Left) { _ = ReleaseCapture(); _ = SendMessage(form.Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            header.Controls.Add(caption); header.Controls.Add(close); form.Controls.Add(header);
            form.TextChanged+=(s,e)=>caption.Text=form.Text;
            Action languageChanged=()=> { if(!form.IsDisposed) close.Text=Localization.Get("Close"); };
            Localization.LanguageChanged+=languageChanged;
            form.Disposed+=(s,e)=>Localization.LanguageChanged-=languageChanged;
            Style(form); caption.ForeColor=Accent;
            form.Paint+=(s,e)=> { using(Pen pen=new Pen(Border,1f)) e.Graphics.DrawRectangle(pen,0,0,form.ClientSize.Width-1,form.ClientSize.Height-1); };
            form.KeyPreview=true;
            form.KeyDown+=(s,e)=> { if(e.KeyCode==Keys.Escape) { dismiss(); e.Handled=true; e.SuppressKeyPress=true; } };
        }

        internal static void Anchor(Form panel,Form map) {
            Screen scr = null;
            try {
                if(map != null && map.IsHandleCreated) scr = Screen.FromControl(map);
                else scr = Screen.PrimaryScreen;
            } catch {
                scr = Screen.PrimaryScreen;
            }
            Rectangle area = scr != null ? scr.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            if(map != null && map.Visible) {
                int x = map.Left - panel.Width - 12;
                if(x < area.Left) x = map.Right + 12;
                panel.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - panel.Width)),
                                           Math.Max(area.Top, Math.Min(map.Top, area.Bottom - panel.Height)));
            } else {
                int x = area.Right - panel.Width - 24;
                int y = area.Top + 60;
                panel.Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, Math.Min(y, area.Bottom - panel.Height)));
            }
        }

        static readonly Font CachedMenuFont=SystemTypography.GetGdiFont(9.25f);
        internal static void Menu(ContextMenuStrip menu) {
            menu.Renderer=new HudMenuRenderer(); menu.ShowImageMargin=false; menu.BackColor=Background; menu.ForeColor=Ink;
            menu.Font=CachedMenuFont; menu.Padding=new Padding(4,5,4,5);
            foreach(ToolStripItem item in menu.Items) {
                if(item is ToolStripSeparator) {
                    item.AutoSize=false;
                    item.Height=9;
                    item.Margin=new Padding(8,2,8,2);
                } else {
                    item.Padding=new Padding(10,3,14,3);
                    item.Margin=new Padding(2,1,2,1);
                    item.ForeColor=Ink;
                }
            }
        }

        sealed class HudMenuRenderer:ToolStripProfessionalRenderer {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Background); }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
                if(e.Item.Selected && e.Item.Enabled) {
                    var rect=new Rectangle(2,1,Math.Max(0,e.Item.Width-5),Math.Max(0,e.Item.Height-3));
                    using(Brush b=new SolidBrush(Surface)) e.Graphics.FillRectangle(b,rect);
                    using(Pen p=new Pen(Border)) e.Graphics.DrawRectangle(p,rect);
                }
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
                int y=e.Item.Height/2;
                using(Pen p=new Pen(Border,1f)) e.Graphics.DrawLine(p,8,y,Math.Max(8,e.Item.Width-9),y);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
                e.TextColor=!e.Item.Enabled?InkMuted:(e.Item.Selected?Accent:Ink);
                base.OnRenderItemText(e);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
                using(Pen p=new Pen(Border)) e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
            }
        }
    }
    // FlowLayoutPanel that prevents automatic snapping/jumping to top when child controls are focused or clicked.
}
