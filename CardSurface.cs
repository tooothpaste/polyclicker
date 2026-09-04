// ===========================================================================
//  CardSurface - every card, drawn
// ---------------------------------------------------------------------------
//  One control paints the whole card list; a real Win32 control exists only
//  while a field is being edited, materialized over the drawn one and torn
//  down on commit - the way Explorer's rename box works.
//
//  Why: a live card was ~27 child windows, and moving one cost a synchronous
//  ~5 ms anchor pass. Resize needed timers, round-robins and catch-up passes
//  to stay ahead of that, and every leak in that scheduling was a new bug -
//  stale spreads, clipped labels, buttons parked mid-card. Here a resize step
//  is one repaint: measured 3 ms per step against 8-25 ms, and every card is
//  pixel-correct on every frame by construction, because painting reads the
//  config directly and there is no second copy of the truth to fall behind.
//
//  The layout rules are CardPanel's, kept verbatim: fields size to content
//  and never grow past what they had, rows spread their slack evenly, X and Y
//  travel together, header icons sit in fixed slots. Visual styles renderers
//  draw the fields, so they are pixel-identical to real Windows controls.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace Polyclicker
{
    // Everything on a card a pointer can touch
    enum El
    {
        None, Name, Gear, Rec, Lock, Remove, Dup, Color, Min, Grip,
        Hotkey, Interval, HoldTgl, Input, Pos, XBox, YBox, Pick, Preview,
        MacroSel, EditMacro, DelMacro, Key, Loop, Speed
    }

    // The chip icons, drawn as strokes rather than font glyphs. Text glyphs
    // came from half a dozen different fonts with half a dozen line weights,
    // and each sat off-center by its own bearing. One pen, one weight, and
    // geometric centering - the row finally reads as a set.
    static class Glyphs
    {
        // During an interactive resize the surface repaints on every WM_SIZE
        // tick; antialiasing is the bulk of that cost. Fast trades it away for
        // the duration of the drag - a crisp repaint follows when it settles.
        public static bool Fast;
        public static SmoothingMode Mode
        {
            get { return Fast ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias; }
        }

        // A glyph is drawn once - antialiased, at its size, in its colours -
        // into a small transparent bitmap, and blitted after that. A paint
        // of a dozen cards places about a hundred glyphs; as strokes that was
        // ~600 GDI+ primitives a frame and most of a drag's cost, and it
        // also means a drag frame's glyphs are as crisp as a still one's.
        static readonly Dictionary<string, Bitmap> glyphCache = new Dictionary<string, Bitmap>();

        public static void Draw(Graphics g, Rectangle r, string kind, Color stroke, Color chipFill)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            string key = kind + "|" + r.Width + "x" + r.Height + "|" + stroke.ToArgb()
                       + "|" + chipFill.ToArgb() + "|" + Theme.Scale.ToString("0.###");
            Bitmap bmp;
            if (!glyphCache.TryGetValue(key, out bmp))
            {
                if (glyphCache.Count > 600)
                {
                    foreach (Bitmap b in glyphCache.Values) b.Dispose();
                    glyphCache.Clear();
                }
                bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var gg = Graphics.FromImage(bmp))
                    DrawStrokes(gg, new Rectangle(0, 0, r.Width, r.Height), kind, stroke, chipFill);
                glyphCache[key] = bmp;
            }
            g.DrawImageUnscaled(bmp, r.X, r.Y);
        }

        static void DrawStrokes(Graphics g, Rectangle r, string kind, Color stroke, Color chipFill)
        {
            // The strokes are authored around a 21 px chip at 100%. A transform
            // centered on the chip scales them - pen width included - so one
            // set of coordinates serves every DPI and UI size.
            var prevT = g.Transform;
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            float cx = 0, cy = 0;
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;      // into the cache: always crisp
            using (var p = new Pen(stroke, 1.6f))
            {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                switch (kind)
                {
                    case "x":
                        g.DrawLine(p, cx - 3.6f, cy - 3.6f, cx + 3.6f, cy + 3.6f);
                        g.DrawLine(p, cx + 3.6f, cy - 3.6f, cx - 3.6f, cy + 3.6f);
                        break;
                    case "minus":
                        g.DrawLine(p, cx - 4.2f, cy, cx + 4.2f, cy);
                        break;
                    case "plus":
                        g.DrawLine(p, cx - 4.2f, cy, cx + 4.2f, cy);
                        g.DrawLine(p, cx, cy - 4.2f, cx, cy + 4.2f);
                        break;
                    case "lock":
                        g.DrawRectangle(p, cx - 4f, cy - 0.5f, 8f, 6f);
                        g.DrawArc(p, cx - 2.7f, cy - 6f, 5.4f, 6f, 180f, 180f);
                        break;
                    case "unlock":
                        g.DrawRectangle(p, cx - 4f, cy - 0.5f, 8f, 6f);
                        // shackle swung open: same arc, hinged up off the body
                        g.DrawArc(p, cx - 0.5f, cy - 7.4f, 5.4f, 6f, 150f, 180f);
                        break;
                    case "gear":
                        // Tune sliders, not a toothed gear - same meaning,
                        // a third of the ink
                        float[] slY = { -3.9f, 0f, 3.9f };
                        float[] slK = { -1.9f, 2.0f, -2.7f };
                        for (int k = 0; k < 3; k++)
                        {
                            g.DrawLine(p, cx - 4.3f, cy + slY[k], cx + 4.3f, cy + slY[k]);
                            using (var b = new SolidBrush(chipFill))
                                g.FillEllipse(b, cx + slK[k] - 2.2f, cy + slY[k] - 2.2f, 4.4f, 4.4f);
                            g.DrawEllipse(p, cx + slK[k] - 1.5f, cy + slY[k] - 1.5f, 3.0f, 3.0f);
                        }
                        break;
                    case "copy":
                        g.DrawRectangle(p, cx - 1.2f, cy - 4.6f, 5.8f, 5.8f);
                        using (var b = new SolidBrush(chipFill))
                            g.FillRectangle(b, cx - 4.9f, cy - 0.9f, 6.6f, 6.6f);
                        g.DrawRectangle(p, cx - 4.6f, cy - 1.2f, 5.8f, 5.8f);
                        break;
                    case "play":
                        g.DrawPolygon(p, new PointF[]
                        {
                            new PointF(cx - 3.0f, cy - 4.6f),
                            new PointF(cx - 3.0f, cy + 4.6f),
                            new PointF(cx + 4.6f, cy),
                        });
                        break;
                    case "up":
                        g.DrawLine(p, cx - 4.2f, cy + 2.2f, cx, cy - 2.6f);
                        g.DrawLine(p, cx, cy - 2.6f, cx + 4.2f, cy + 2.2f);
                        break;
                    case "down":
                        g.DrawLine(p, cx - 4.2f, cy - 2.2f, cx, cy + 2.6f);
                        g.DrawLine(p, cx, cy + 2.6f, cx + 4.2f, cy - 2.2f);
                        break;
                    case "timeline":
                        // Three staggered bars: an event list at a glance
                        g.DrawLine(p, cx - 4.4f, cy - 3.6f, cx + 4.4f, cy - 3.6f);
                        g.DrawLine(p, cx - 4.4f, cy, cx + 1.6f, cy);
                        g.DrawLine(p, cx - 4.4f, cy + 3.6f, cx + 3.2f, cy + 3.6f);
                        break;
                    case "pencil":
                        g.DrawLine(p, cx - 4.2f, cy + 4.2f, cx - 3.2f, cy + 1.4f);   // tip
                        g.DrawLine(p, cx - 3.2f, cy + 1.4f, cx + 2.4f, cy - 4.2f);   // one edge
                        g.DrawLine(p, cx - 1.4f, cy + 3.2f, cx + 4.2f, cy - 2.4f);   // other edge
                        g.DrawLine(p, cx - 4.2f, cy + 4.2f, cx - 1.4f, cy + 3.2f);
                        g.DrawLine(p, cx + 2.4f, cy - 4.2f, cx + 4.2f, cy - 2.4f);   // cap
                        break;
                    case "trash":
                        // Minimal can: lid, floating handle, tapered body -
                        // no ribs, nothing fighting at this size
                        g.DrawLine(p, cx - 4.6f, cy - 3.4f, cx + 4.6f, cy - 3.4f);   // lid
                        g.DrawLine(p, cx - 1.5f, cy - 5.4f, cx + 1.5f, cy - 5.4f);   // handle
                        g.DrawLine(p, cx - 3.4f, cy - 1.4f, cx - 2.7f, cy + 4.4f);   // body
                        g.DrawLine(p, cx + 3.4f, cy - 1.4f, cx + 2.7f, cy + 4.4f);
                        g.DrawLine(p, cx - 2.7f, cy + 4.4f, cx + 2.7f, cy + 4.4f);
                        break;
                    case "rec":
                        // A dot inside a ring, not a bare dot. The colour
                        // swatch two chips away is also a filled dot, and at
                        // this size the two read as the same control; the ring
                        // also gives it the stroke weight every other glyph
                        // here is drawn with, so it sits with them instead of
                        // reading as a heavier blob.
                        using (var b = new SolidBrush(stroke))
                            g.FillEllipse(b, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
                        g.DrawEllipse(p, cx - 5.2f, cy - 5.2f, 10.4f, 10.4f);
                        break;
                    case "eye":
                        // almond lids + pupil, same stroke weight as the rest
                        g.DrawArc(p, cx - 4.8f, cy - 5.4f, 9.6f, 8.0f, 200, 140);
                        g.DrawArc(p, cx - 4.8f, cy - 2.6f, 9.6f, 8.0f, 20, 140);
                        g.DrawEllipse(p, cx - 1.4f, cy - 1.4f, 2.8f, 2.8f);
                        break;
                    case "pin":
                        g.DrawEllipse(p, cx - 3f, cy - 5.4f, 6f, 6f);
                        g.DrawLine(p, cx - 2.1f, cy - 0.4f, cx, cy + 4.6f);
                        g.DrawLine(p, cx + 2.1f, cy - 0.4f, cx, cy + 4.6f);
                        break;
                    case "loop":
                        // circular arrow: repeats until stopped
                        g.DrawArc(p, cx - 4.2f, cy - 4.2f, 8.4f, 8.4f, -60f, 300f);
                        g.DrawLine(p, cx - 2.1f, cy - 3.6f, cx - 3.1f, cy - 0.8f);
                        g.DrawLine(p, cx - 2.1f, cy - 3.6f, cx - 5.1f, cy - 4.2f);
                        break;
                    case "once":
                        // straight arrow: plays through a single time
                        g.DrawLine(p, cx - 4.6f, cy, cx + 4.2f, cy);
                        g.DrawLine(p, cx + 4.2f, cy, cx + 1.4f, cy - 2.8f);
                        g.DrawLine(p, cx + 4.2f, cy, cx + 1.4f, cy + 2.8f);
                        break;
                    case "target":
                        g.DrawEllipse(p, cx - 3.2f, cy - 3.2f, 6.4f, 6.4f);
                        g.DrawLine(p, cx, cy - 5.4f, cx, cy - 3.2f);
                        g.DrawLine(p, cx, cy + 3.2f, cx, cy + 5.4f);
                        g.DrawLine(p, cx - 5.4f, cy, cx - 3.2f, cy);
                        g.DrawLine(p, cx + 3.2f, cy, cx + 5.4f, cy);
                        using (var b = new SolidBrush(stroke))
                            g.FillEllipse(b, cx - 1.1f, cy - 1.1f, 2.2f, 2.2f);
                        break;
                    case "mouse":
                        using (var path = new GraphicsPath())
                        {
                            var mr = new RectangleF(cx - 3.2f, cy - 5.2f, 6.4f, 10.4f);
                            path.AddArc(mr.X, mr.Y, mr.Width, mr.Width, 180, 180);
                            path.AddArc(mr.X, mr.Bottom - mr.Width, mr.Width, mr.Width, 0, 180);
                            path.CloseFigure();
                            g.DrawPath(p, path);
                        }
                        g.DrawLine(p, cx, cy - 5.2f, cx, cy - 1.6f);
                        break;
                    case "toggle-on":
                    case "toggle-off":
                    {
                        // The kill switch is a mode, so it draws as a switch. The
                        // knob side says which way it is thrown without relying on
                        // the chip colour alone.
                        bool on = kind == "toggle-on";
                        var track = new RectangleF(cx - 7f, cy - 4.4f, 14f, 8.8f);
                        using (var tp = new GraphicsPath())
                        {
                            tp.AddArc(track.X, track.Y, track.Height, track.Height, 90, 180);
                            tp.AddArc(track.Right - track.Height, track.Y, track.Height, track.Height, 270, 180);
                            tp.CloseFigure();
                            g.DrawPath(p, tp);
                        }
                        using (var b = new SolidBrush(stroke))
                            g.FillEllipse(b, on ? cx + 1.4f : cx - 5.4f, cy - 2.6f, 5.2f, 5.2f);
                        break;
                    }
                    case "stop":
                        using (var b = new SolidBrush(stroke))
                            g.FillRectangle(b, cx - 3.4f, cy - 3.4f, 6.8f, 6.8f);
                        break;
                    case "save":
                        // arrow dropping into an open tray - reads instantly
                        // where the floppy went muddy at 20 px
                        g.DrawLine(p, cx - 4.8f, cy + 1.2f, cx - 4.8f, cy + 4.6f);
                        g.DrawLine(p, cx - 4.8f, cy + 4.6f, cx + 4.8f, cy + 4.6f);
                        g.DrawLine(p, cx + 4.8f, cy + 4.6f, cx + 4.8f, cy + 1.2f);
                        g.DrawLine(p, cx, cy - 5.4f, cx, cy + 1.4f);
                        g.DrawLine(p, cx - 2.6f, cy - 1f, cx, cy + 1.6f);
                        g.DrawLine(p, cx + 2.6f, cy - 1f, cx, cy + 1.6f);
                        break;
                    case "folder":
                        g.DrawPolygon(p, new PointF[]
                        {
                            new PointF(cx - 4.8f, cy + 3.8f), new PointF(cx - 4.8f, cy - 3.2f),
                            new PointF(cx - 1.4f, cy - 3.2f), new PointF(cx - 0.2f, cy - 1.6f),
                            new PointF(cx + 4.8f, cy - 1.6f), new PointF(cx + 4.8f, cy + 3.8f),
                        });
                        break;
                    case "undo":
                        g.DrawArc(p, cx - 3.8f, cy - 3.8f, 7.6f, 7.6f, -180f, 225f);
                        g.DrawLine(p, cx - 3.8f, cy - 1.6f, cx - 5.6f, cy - 0.2f);
                        g.DrawLine(p, cx - 3.8f, cy - 1.6f, cx - 1.6f, cy - 0.6f);
                        break;
                }
            }
            g.Transform = prevT;
            g.SmoothingMode = prev;
        }

        // The rounded chip itself - fill, border, glyph - shared between the
        // drawn cards and any real control that wants to look like one
        public static void Chip(Graphics g, Rectangle r, string kind, bool hot, Theme.ChipStyle cs)
        {
            Color back = hot ? cs.Hot : cs.Back;
            Theme.RoundedBox(g, r, Theme.S(4), back, cs.Line);
            Draw(g, r, kind, cs.Text, back);
        }

        // The dropdown chevron, apex 3.5 (scaled) below (cx, cy): the same
        // stroke on every drawn combo, in the card rows and the profile menu
        public static void Chevron(Graphics g, float cx, float cy, Color color)
        {
            var prev = g.SmoothingMode;
            var prevT = g.Transform;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(Theme.Scale, Theme.Scale);
            using (var p = new Pen(color, 1.6f))
            {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(p, -3.5f, 0, 0, 3.5f);
                g.DrawLine(p, 3.5f, 0, 0, 3.5f);
            }
            g.Transform = prevT;
            g.SmoothingMode = prev;
        }
    }

    sealed class CardSurface : Control
    {
        // Every layout number here is authored at 100% and read through S(),
        // so the cards follow the display DPI and the UI-size setting
        static int S(int px) { return Theme.S(px); }
        public static int MinWidth { get { return S(495); } }
        static int FullHeight { get { return S(132); } }
        static int ShortHeight { get { return S(98); } }
        static int RolledHeight { get { return S(44); } }
        static int RowSpace { get { return S(18); } }   // vertical gap between rows
        static int Pad { get { return S(10); } }
        static int IconW { get { return S(32); } }
        static int IconPitch { get { return S(42); } }
        static int IconH { get { return S(21); } }
        static int MarginL { get { return S(6); } }
        // No right margin: the bar gutter is the margin, so the cards end on
        // the same line as the gear and the strip buttons, 12 px in
        static int MarginR { get { return 0; } }
        static int Gut { get { return S(6); } }
        static int TopPad { get { return S(6); } }
        static int RowGap { get { return S(34); } }
        static int GripW { get { return S(15); } }       // the reorder handle strip

        // All color choices live in Theme, so the cards follow the dark toggle
        static Color Muted    { get { return Theme.MutedText; } }
        static Color RunGreen { get { return Theme.RunGreen; } }
        static Color Ready    { get { return Theme.ReadyBlue; } }
        static Color ArmedRed { get { return Theme.ArmedRed; } }
        static Color OffText  { get { return Theme.OffText; } }
        static Color CardLine { get { return Theme.CardLine; } }

        internal sealed class Card
        {
            public SlotConfig Cfg;
            public string Status = "";
            public Color StatusColor = Color.Gray;
            public bool StatusBold;
            public DateTime NoticeUntil = DateTime.MinValue;
            public bool Armed, Recording;
            public bool GateMissing;                     // gate names a window nobody has open
            public int TakeEvents;
            public Rectangle Bounds;                     // in scrolled space
            public readonly Dictionary<El, Rectangle> Hit = new Dictionary<El, Rectangle>();
            public readonly List<DrawnLabel> Labels = new List<DrawnLabel>();
        }

        readonly List<Card> cards = new List<Card>();
        string[] macroNames = new string[0];
        int scrollY;

        // The one live editor
        Control editor;
        El editorEl = El.None;
        int editorIndex = -1;
        string editorOriginal;

        // Hover
        int hotIndex = -1;
        El hotEl = El.None;
        readonly ToolTip tips;

        Font labelFont, boldFont;
        readonly Dictionary<string, int> measure = new Dictionary<string, int>();

        public string RecordKeyName = "the record hotkey";

        // Every hotkey suspended. A card that still reads "press 3 to start"
        // while nothing can start is worse than no status at all.
        bool _allOff;
        public bool AllHotkeysOff
        {
            get { return _allOff; }
            set
            {
                if (_allOff == value) return;
                _allOff = value;
                for (int i = 0; i < cards.Count; i++) RefreshStatus(i, false, 0);
                Invalidate();
            }
        }

        public event Action<int> Changed;
        public event Action<int> RemoveClicked;
        public event Action<int> DuplicateClicked;
        public event Action<int> OptionsClicked;
        public event Action<int> RecordClicked;
        public event Action<int> PickPosClicked;
        public event Action<int> PreviewClicked;
        public event Action<int> EditMacroClicked;
        public event Action<int> DeleteMacroClicked;
        public event Action<int> ColorClicked;

        public CardSurface(ToolTip sharedTips)
        {
            tips = sharedTips;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            // No ResizeRedraw: OnResize decides for itself. Height changes
            // never move a card (masonry flows from width alone), so a
            // vertical drag paints nothing but the newly exposed strip.
            // 20 ms: one frame of cheap paint after the last size tick, then
            // crisp - short enough that a pause mid-drag never shows jaggies
            settle.Interval = 20;
            settle.Tick += delegate
            {
                settle.Stop();
                Glyphs.Fast = false;
                // One crisp pass over everything that painted jaggy during
                // the drag - the strip buttons ride along with the edge too
                if (Parent != null) Parent.Invalidate(true);
                else Invalidate();
            };
        }

        readonly Timer settle = new Timer();
        int paintedWidth = -1;

        protected override void Dispose(bool disposing)
        {
            if (disposing) { DropText(); if (memDc != IntPtr.Zero) { DeleteDC(memDc); memDc = IntPtr.Zero; } }
            if (disposing) settle.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            DropText();                     // keyed by Font object; these are going away
            if (labelFont != null) labelFont.Dispose();
            if (boldFont != null) boldFont.Dispose();
            labelFont = new Font(Font.FontFamily, Theme.Sf(11f), GraphicsUnit.Pixel);  // 8.25 pt
            boldFont = new Font(Font, FontStyle.Bold);
        }

        // --- the model --------------------------------------------------------

        public int Count { get { return cards.Count; } }
        public SlotConfig CfgAt(int i) { return cards[i].Cfg; }
        internal Card At(int i) { return cards[i]; }

        public void Add(SlotConfig cfg) { Insert(cards.Count, cfg); }

        // EndEdit comes FIRST in all three: an open editor holds a card index,
        // and reverting through it after the list has shifted writes the old
        // value into whatever card slid into that position - or off the end of
        // a just-cleared list.
        public void Insert(int at, SlotConfig cfg)
        {
            EndEdit(false);
            var c = new Card();
            c.Cfg = cfg;
            cards.Insert(at, c);
            Reflow();
        }

        public void RemoveAt(int i)
        {
            EndEdit(false);
            cards.RemoveAt(i);
            Reflow();
        }

        public void Clear()
        {
            EndEdit(false);
            cards.Clear();
            Reflow();
        }

        public void SetMacroList(string[] names) { macroNames = names ?? new string[0]; }
        public Action MacroListOpening;         // the owner rescans the folder

        // Anything about a card's config changed from outside (dialog, pick
        // pos, recording landed): geometry may differ, so recompute and repaint
        public void Reflow()
        {
            UpdateScrollInfo();
            Invalidate();
        }

        // --- status lines -----------------------------------------------------

        public void Notice(int i, string text)
        {
            Card c = cards[i];
            c.NoticeUntil = DateTime.Now.AddSeconds(3);
            c.Status = text;
            c.StatusColor = Theme.NoticeRed;
            c.StatusBold = false;
            InvalidateCard(i);
        }

        // A notice only blocks RefreshStatus while it lasts; nothing recomputes
        // an idle card afterwards, so the timer asks who just timed out.
        public bool NoticeExpired(int i)
        {
            Card c = cards[i];
            if (c.NoticeUntil == DateTime.MinValue || DateTime.Now < c.NoticeUntil)
                return false;
            c.NoticeUntil = DateTime.MinValue;
            return true;
        }

        public void SetArmed(int i, bool on)
        {
            cards[i].Armed = on;
            RefreshStatus(i, false, 0);
            InvalidateCard(i);
        }


        // The caller owns the once-a-second window scan; this just records the
        // verdict and says whether the status line needs recomputing.
        public bool SetGateMissing(int i, bool missing)
        {
            if (cards[i].GateMissing == missing) return false;
            cards[i].GateMissing = missing;
            return true;
        }

        public bool IsGateMissing(int i) { return cards[i].GateMissing; }

        public void SetRecording(int i, bool on, int events)
        {
            cards[i].Recording = on;
            cards[i].TakeEvents = events;
            RefreshStatus(i, false, 0);
        }

        // pendingSecs >= 0 means the slot is armed but still counting down a
        // start delay or scheduled time; -1 is the normal case. Optional so
        // the many idle-path callers don't all have to say "-1".
        public void RefreshStatus(int i, bool running, double actualCps, int pendingSecs = -1)
        {
            Card c = cards[i];
            if (DateTime.Now < c.NoticeUntil) return;
            string text; Color color; bool bold = false;
            SlotConfig s = c.Cfg;

            // One wording for expanded and collapsed cards alike - the folded
            // line has room for the full sentence, so it says the full sentence
            string egate = s.IsGated
                ? "  (" + s.GateShort() + (c.GateMissing ? " isn't open" : "") + ")" : "";
            if (c.Recording)
            {
                text = "⏺ Recording - " + c.TakeEvents + " events  (stop: " + RecordKeyName + ")";
                color = Theme.NoticeRed; bold = true;
            }
            else if (running && pendingSecs >= 0)
            {
                // Armed, waiting out its start delay or scheduled time. The
                // hotkey hint matters here: cancelling IS that same hotkey.
                text = "⏳ Starts in " + FmtWait(pendingSecs)
                     + (s.Hotkey.Trim().Length > 0
                        ? "  (" + (s.Mode == "Hold" ? "releasing " : "")
                          + HotkeyParser.Parse(s.Hotkey) + " cancels)" : "") + egate;
                color = RunGreen; bold = true;
            }
            else if (running)
            {
                double goal = s.Interval > 0 ? 1000.0 / s.Interval : 0;
                text = (s.IsMacro
                     ? "▶ Playing" + (s.MacroSpeed != 100
                            ? " at " + (s.MacroSpeed / 100.0).ToString("0.##") + "×" : "")
                     : s.HoldDown
                     ? "▶ Holding"
                     : "▶ " + actualCps.ToString("0.#") + " / " + goal.ToString("0.#") + " cps") + egate;
                color = RunGreen; bold = true;
            }
            else if (c.Armed)
            {
                text = "⏺ Ready to record (" + RecordKeyName + ")";
                color = ArmedRed;
            }
            else if (_allOff)
            {
                text = "⏸ Hotkeys off";
                color = OffText;
            }
            else if (!s.HotkeyOff && s.IsGated && c.GateMissing)
            {
                text = "⚠ " + s.GateShort() + " isn't open - it only clicks there";
                color = Theme.WarnText;
            }
            else if (s.Hotkey.Trim().Length > 0)
            {
                if (s.HotkeyOff)
                {
                    text = "🔒 Hotkey off (" + HotkeyParser.Parse(s.Hotkey) + ")";
                    color = OffText;
                }
                else
                {
                    text = "● Ready ("
                         + (s.Mode == "Hold" ? "hold " : "press ") + HotkeyParser.Parse(s.Hotkey)
                         + ")" + egate;
                    color = Ready;
                }
            }
            else
            {
                text = "● Set a hotkey" + egate;
                color = OffText;
            }

            if (text != c.Status || color != c.StatusColor || bold != c.StatusBold)
            {
                c.Status = text; c.StatusColor = color; c.StatusBold = bold;
                Rectangle r;
                if (c.Hit.TryGetValue(El.None, out r)) Invalidate(ToView(r));   // status rect
                else InvalidateCard(i);
            }
        }

        // A countdown reads best in the largest unit that moves: seconds
        // under a minute, then minutes, then hours - never "5400s".
        static string FmtWait(int secs)
        {
            if (secs >= 3600) return (secs / 3600) + "h " + (secs % 3600) / 60 + "m";
            if (secs >= 60) return (secs / 60) + "m " + (secs % 60).ToString("00") + "s";
            return secs + "s";
        }

        void InvalidateCard(int i)
        {
            if (i >= 0 && i < cards.Count) Invalidate(ToView(cards[i].Bounds));
        }

        Rectangle ToView(Rectangle scrolled)
        {
            scrolled.Offset(0, -scrollY);
            scrolled.Inflate(2, 2);
            return scrolled;
        }

        // --- geometry ---------------------------------------------------------

        int CardHeight(SlotConfig s)
        {
            if (s.Collapsed) return RolledHeight;
            return s.IsCustomKey ? ShortHeight : FullHeight;
        }

        int Cols()
        {
            int avail = Math.Max(MinWidth, ViewW - MarginL - MarginR);
            return Math.Max(1, (avail + Gut) / (MinWidth + Gut));
        }

        // Heights vary per card (collapsed vs full), so cards pack like
        // masonry: each card in order drops into whichever column is currently
        // shortest. A collapsed card no longer holds a whole row hostage - the
        // next card moves up under it. With equal heights this reduces to the
        // old row grid exactly. Fills every card's Bounds; returns the total
        // content height.
        int FlowBounds()
        {
            int cols = Cols();
            int avail = Math.Max(MinWidth, ViewW - MarginL - MarginR);
            int cardW = (avail - (cols - 1) * Gut) / cols;
            var colY = new int[cols];
            for (int k = 0; k < cols; k++) colY[k] = TopPad;
            int bottom = TopPad;
            for (int i = 0; i < cards.Count; i++)
            {
                int best = 0;
                for (int k = 1; k < cols; k++) if (colY[k] < colY[best]) best = k;
                int h = CardHeight(cards[i].Cfg);
                cards[i].Bounds = new Rectangle(MarginL + best * (cardW + Gut),
                                                colY[best], cardW, h);
                colY[best] += h + RowSpace;
                if (colY[best] > bottom) bottom = colY[best];
            }
            return bottom + S(8);
        }

        int ContentHeight() { return FlowBounds(); }

        int Fit(string text, Font f, int max, int pad, int min)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            string key = f.Size.ToString("0.##") + "|" + text;
            int w;
            if (!measure.TryGetValue(key, out w))
            {
                w = TextRenderer.MeasureText(text, f).Width;
                if (measure.Count > 600) measure.Clear();
                measure[key] = w;
            }
            return Math.Max(min, Math.Min(max, w + pad));
        }

        static int ComboPad { get { return BarGutter + S(20); } }

        // A label's slot, measured in the label font - fixed guesses left
        // uneven gaps once the font changed. Slot padding stays at 1 so the
        // label hugs its control: the row gap supplies the remaining 4.
        int LabelW(string text)
        {
            return Fit(text, labelFont ?? Font, S(130), 1, S(8));
        }

        // Fills card.Bounds and card.Hit for the current width. Pure math -
        // nothing here touches a window.
        void LayoutCard(Card c, Rectangle r)
        {
            c.Bounds = r;
            var hit = c.Hit;
            hit.Clear();
            c.Labels.Clear();
            SlotConfig s = c.Cfg;
            // Everything starts to the right of the grip column, rows and
            // name alike
            int L = r.X + S(2) + GripW + S(4), R = r.Right - Pad;
            int top = r.Y;
            int lineA = top + S(26), lineB = lineA + RowGap, lineC = lineB + RowGap;

            // A collapsed card is one line, vertically centered in its pill.
            // Expanded cards' edge rows sit dead-center ON the border: the pill
            // top is at +8 and the row is 21 tall, so the row starts at -2.
            int rowY = s.Collapsed ? top + S(15) : top - S(2);

            // Edge buttons, right to left: remove, then collapse beside it,
            // then the lock; record (macro cards) beyond that. The bottom pair
            // (gear, duplicate) mirrors the row over the lower border.
            int x = R - IconW;
            hit[El.Remove] = new Rectangle(x, rowY, IconW, IconH); x -= IconPitch;
            hit[El.Min] = new Rectangle(x, rowY, IconW, IconH);    x -= IconPitch;
            hit[El.Lock] = new Rectangle(x, rowY, IconW, IconH);   x -= IconPitch;
            if (s.IsMacro && !s.Collapsed) { hit[El.Rec] = new Rectangle(x, rowY, IconW, IconH); x -= IconPitch; }

            // The reorder handle is the card's whole left edge, dots centered -
            // easy to find, easy to grab, whatever the card's height
            hit[El.Grip] = new Rectangle(r.X + S(2), top + S(10), GripW, CardHeight(s) - S(14));

            // After the name: the gear, then the color dot - the card's own
            // controls cluster at its top left, leaving the bottom edge clear
            string shownName = s.Name.Length > 0 ? s.Name : "Auto-Clicker #?";
            int nameW = Fit(shownName, Font, x - S(8) - L - S(54), S(12), S(60));
            hit[El.Name] = new Rectangle(L, rowY, nameW, IconH);
            hit[El.Gear] = new Rectangle(L + nameW + S(6), rowY, IconH, IconH);
            hit[El.Color] = new Rectangle(L + nameW + S(6) + IconH + S(6), rowY, IconH, IconH);

            if (s.Collapsed)
            {
                // Everything else the card would show, folded into the line:
                // what it does (input) and where it stands (status). The same
                // 12 px on both sides of the input, so it reads as one rhythm
                int ix = L + nameW + S(6) + IconH + S(6) + IconH + S(12);
                string what = s.IsMacro ? "Macro" : s.IsCustomKey ? "Key " + s.CustomKey : s.Input;
                c.Labels.Add(new DrawnLabel(what, new Point(ix, rowY + S(4))));
                int wW = Fit(what, labelFont ?? Font, S(120), 0, S(10));
                int sx = ix + wW + S(12);
                hit[El.None] = new Rectangle(sx, rowY, Math.Max(S(40), x + IconPitch - S(8) - sx), IconH);
                return;
            }

            // Row A: hotkey, interval, input share the slack. Label slots are
            // measured, not guessed, so every label-to-field gap is the same.
            string hkText = s.Hotkey.Trim().Length > 0 ? HotkeyParser.Parse(s.Hotkey).ToString() : "None";
            int hkW = Fit(hkText, Font, S(88), S(16), S(44));
            int inW = Fit(s.Input, Font, S(150), ComboPad, S(60));
            // Macro cards trade the interval for what a macro actually tunes:
            // whether it repeats, and how fast it replays. The loop gap moved
            // to the advanced dialog with the other between-repeats settings.
            if (s.IsMacro)
                SpreadRow(lineA, L, R, hit, new RowGroup[]
                {
                    new RowGroup(new RowItem("Hotkey", El.None, LabelW("Hotkey"), 4),
                                 new RowItem(null, El.Hotkey, hkW, 0)),
                    new RowGroup(new RowItem("Loop", El.None, LabelW("Loop"), 4),
                                 new RowItem(null, El.Loop, S(44), 0)),
                    new RowGroup(new RowItem("Speed (%)", El.None, LabelW("Speed (%)"), 4),
                                 new RowItem(null, El.Speed, S(52), 0)),
                    new RowGroup(new RowItem("Input", El.None, LabelW("Input"), 4),
                                 new RowItem(null, El.Input, inW, 0)),
                }, c);
            else
                SpreadRow(lineA, L, R, hit, new RowGroup[]
                {
                    new RowGroup(new RowItem("Hotkey", El.None, LabelW("Hotkey"), 4),
                                 new RowItem(null, El.Hotkey, hkW, 0)),
                    new RowGroup(new RowItem("Interval (ms)", El.None, LabelW("Interval (ms)"), 4),
                                 new RowItem(null, El.Interval, S(52), 0),
                                 new RowItem(null, El.HoldTgl, S(30), 0)),
                    new RowGroup(new RowItem("Input", El.None, LabelW("Input"), 4),
                                 new RowItem(null, El.Input, inW, 0)),
                }, c);

            if (s.IsMacro)
            {
                c.Labels.Add(new DrawnLabel("Macro", new Point(L, lineB + S(4))));
                hit[El.DelMacro] = new Rectangle(R - S(32), lineB - 1, S(32), S(23));
                hit[El.EditMacro] = new Rectangle(R - S(68), lineB - 1, S(32), S(23));
                int mx = L + LabelW("Macro") + S(4);
                hit[El.MacroSel] = new Rectangle(mx, lineB, Math.Max(S(60), R - S(76) - mx), IconH);
            }
            else if (!s.IsCustomKey)
            {
                // Position is a two-state fact, so it's a toggle, not a
                // dropdown: ⌖ clicks a fixed spot, 🖱 follows the mouse.
                // X and Y grow past their default width only when the number
                // no longer fits it.
                var groups = s.IsFixed
                    ? new RowGroup[]
                      {
                          new RowGroup(new RowItem("Position", El.None, LabelW("Position"), 4),
                                       new RowItem(null, El.Pos, S(44), 0)),
                          new RowGroup(new RowItem("X", El.None, LabelW("X"), 4),
                                       new RowItem(null, El.XBox, Fit(s.X.ToString(CultureInfo.InvariantCulture), Font, S(84), S(14), S(48)), 0, S(12)),
                                       new RowItem("Y", El.None, LabelW("Y"), 4),
                                       new RowItem(null, El.YBox, Fit(s.Y.ToString(CultureInfo.InvariantCulture), Font, S(84), S(14), S(48)), 0)),
                          new RowGroup(new RowItem(null, El.Preview, S(24), 0)),
                          new RowGroup(new RowItem(null, El.Pick, S(36), -1)),
                      }
                    : new RowGroup[]
                      {
                          new RowGroup(new RowItem("Position", El.None, LabelW("Position"), 4),
                                       new RowItem(null, El.Pos, S(44), 0)),
                      };
                // Together at the right, not spread across the card - these
                // parts read as one instrument
                PackRowRight(lineB, L, R, hit, groups, c);
            }

            int tailY = s.IsCustomKey ? lineB : lineC;
            if (s.IsCustomKey)
            {
                c.Labels.Add(new DrawnLabel("Key", new Point(L, tailY + S(4))));
                hit[El.Key] = new Rectangle(L + LabelW("Key") + S(4), tailY, S(88), IconH);
            }
            int statusX = s.IsCustomKey ? L + LabelW("Key") + S(4) + S(88) + S(12) : L;
            hit[El.None] = new Rectangle(statusX, tailY + S(5), Math.Max(S(60), R - S(44) - statusX), S(20));

            // Duplicate sits fully inside the card's bottom-right corner -
            // nothing rides the lower border, so stacked cards never crowd
            hit[El.Dup] = new Rectangle(R - IconW, r.Y + CardHeight(s) - IconH - S(7), IconW, IconH);
        }

        internal struct DrawnLabel
        {
            public string Text; public Point At;
            public DrawnLabel(string t, Point a) { Text = t; At = a; }
        }

        struct RowItem
        {
            public string Label; public El El; public int W, Dy, GapAfter;
            public RowItem(string label, El el, int w, int dy) { Label = label; El = el; W = w; Dy = dy; GapAfter = S(4); }
            public RowItem(string label, El el, int w, int dy, int gap) { Label = label; El = el; W = w; Dy = dy; GapAfter = gap; }
        }

        struct RowGroup
        {
            public RowItem[] Items;
            public RowGroup(params RowItem[] items) { Items = items; }
            public int Width
            {
                get
                {
                    int w = 0;
                    for (int i = 0; i < Items.Length; i++)
                        w += Items[i].W + (i < Items.Length - 1 ? Items[i].GapAfter : 0);
                    return w;
                }
            }
        }

        void SpreadRow(int y, int L, int R, Dictionary<El, Rectangle> hit, RowGroup[] groups, Card c)
        {
            int total = 0;
            foreach (RowGroup g in groups) total += g.Width;
            int gap = groups.Length > 1
                ? Math.Max(S(12), ((R - L) - total) / (groups.Length - 1))
                : 0;
            LayRow(y, L, gap, hit, groups, c);
        }

        // The same row, but packed shoulder-to-shoulder against the right
        // edge instead of justified - for rows whose parts belong together
        void PackRowRight(int y, int L, int R, Dictionary<El, Rectangle> hit, RowGroup[] groups, Card c)
        {
            int gap = S(14);
            int total = gap * (groups.Length - 1);
            foreach (RowGroup g in groups) total += g.Width;
            LayRow(y, Math.Max(L, R - total), gap, hit, groups, c);
        }

        void LayRow(int y, int x, int gap, Dictionary<El, Rectangle> hit, RowGroup[] groups, Card c)
        {
            foreach (RowGroup g in groups)
            {
                foreach (RowItem it in g.Items)
                {
                    if (it.Label != null)
                        c.Labels.Add(new DrawnLabel(it.Label, new Point(x, y + S(4))));
                    else
                    {
                        int h = it.El == El.Pick ? S(23) : IconH;
                        hit[it.El] = new Rectangle(x, y + (it.Dy < 0 ? -S(-it.Dy) : S(it.Dy)), it.W, h);
                    }
                    x += it.W + it.GapAfter;
                }
                x += gap - S(4);   // group gap replaces the last item's trailing gap
            }
        }

        // --- painting ---------------------------------------------------------

        // True between WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE on the main
        // window - the OS's own word that a frame drag is in progress. The
        // perf harness sets it to measure a drag without a mouse.
        public static bool Interactive;

        // Counters for the perf harness: paints, and ticks spent painting and
        // laying out. Cheap enough to leave in.
        public static int PaintCount;
        public static long PaintTicks, LayoutTicks;

        protected override void OnPaint(PaintEventArgs pe)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Graphics g = pe.Graphics;
            g.Clear(BackColor);
            if (labelFont == null) OnFontChanged(EventArgs.Empty);

            FlowBounds();
            paintedWidth = ClientSize.Width;
            for (int i = 0; i < cards.Count; i++)
            {
                Card c = cards[i];
                var view = c.Bounds; view.Offset(0, -scrollY);
                if (view.Bottom < 0 || view.Top > ClientSize.Height)
                    continue;                // Bounds already set - stays hit-testable
                if (!view.IntersectsWith(pe.ClipRectangle))
                    continue;                // laid out on its last real paint
                c.Labels.Clear();
                long l0 = System.Diagnostics.Stopwatch.GetTimestamp();
                LayoutCard(c, c.Bounds);
                LayoutTicks += System.Diagnostics.Stopwatch.GetTimestamp() - l0;
                DrawCard(g, c, i);
            }
            DrawBar(g);
            long p1 = System.Diagnostics.Stopwatch.GetTimestamp();
            FlushBoxes(g);
            long p2 = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (Action<Graphics> a in later) a(g);
            later.Clear();
            long p3 = System.Diagnostics.Stopwatch.GetTimestamp();
            FlushText(g);
            long p4 = System.Diagnostics.Stopwatch.GetTimestamp();
            PhaseCardsTicks += p1 - t0; PhaseBoxTicks += p2 - p1;
            PhaseGlyphTicks += p3 - p2; PhaseTextTicks += p4 - p3;
            PaintCount++;
            PaintTicks += p4 - t0;
        }

        public static long PhaseCardsTicks, PhaseBoxTicks, PhaseGlyphTicks, PhaseTextTicks;

        // --- the drag-frame path ------------------------------------------------
        // Crisp paints draw each box where it comes, antialiased through
        // GDI+. During a frame drag GDI+'s per-shape overhead is most of the
        // paint, so a fast frame queues its boxes and draws them all through
        // plain GDI in one device-context session, then the strokes that sit
        // on them (glyphs, chevrons, swatch dots), then the text. Card bodies
        // are the one thing drawn in place either way: everything else on
        // the card sits on top of them.
        struct BoxItem { public Rectangle R; public int Rad; public Color Fill, Line; }
        readonly List<BoxItem> boxes = new List<BoxItem>();
        readonly List<Action<Graphics>> later = new List<Action<Graphics>>();

        void Box(Graphics g, Rectangle r, int rad, Color fill, Color line)
        {
            if (!Glyphs.Fast) { Theme.RoundedBox(g, r, rad, fill, line); return; }
            var b = new BoxItem();
            b.R = r; b.Rad = rad; b.Fill = fill; b.Line = line;
            boxes.Add(b);
        }

        void Later(Graphics g, Action<Graphics> draw)
        {
            if (Glyphs.Fast) later.Add(draw); else draw(g);
        }

        void Chip(Graphics g, Rectangle r, string kind, bool hot, Theme.ChipStyle cs)
        {
            Color back = hot ? cs.Hot : cs.Back;
            Box(g, r, S(4), back, cs.Line);
            Later(g, delegate(Graphics gg) { Glyphs.Draw(gg, r, kind, cs.Text, back); });
        }

        void FlushBoxes(Graphics g)
        {
            if (boxes.Count == 0) return;
            IntPtr hdc = g.GetHdc();
            try
            {
                for (int i = 0; i < boxes.Count; i++)
                    Theme.GdiRoundedBox(hdc, boxes[i].R, boxes[i].Rad, boxes[i].Fill, boxes[i].Line);
            }
            finally { g.ReleaseHdc(hdc); }
            boxes.Clear();
        }

        // --- text, drawn last and all at once ---------------------------------
        // GDI text is the paint: DrawTextEx costs ~8 us a string whatever
        // wraps it, and a dozen cards are 240 strings a frame. Text always
        // sits on a flat fill, so each (string, font, color, fill) is drawn
        // once into a screen-format bitmap - the same DrawTextEx, the same
        // HFONT, the same overhang margins TextRenderer passes (ceil(h/6)
        // left, 1.5x that right) - and blitted from then on, a couple of us
        // each. Strings that don't fit their box (ellipsis) take the direct
        // route, with TextRenderer's own vertical centring, so either way
        // the pixels are the ones it would have drawn.
        struct TextItem
        {
            public string Text; public Font Font; public Color Color, Back;
            public Rectangle Rect; public Point At; public bool AtPoint, Direct;
            public TextFormatFlags Flags;
        }
        readonly List<TextItem> texts = new List<TextItem>();

        // A string that changes every tick (a running card's status) is
        // drawn in place rather than minted into the cache each time
        void TextNow(string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            var t = new TextItem();
            t.Text = s; t.Font = f; t.Rect = r; t.Color = c; t.Flags = flags; t.Direct = true;
            texts.Add(t);
        }

        void TextLater(string s, Font f, Point at, Color c, Color back)
        {
            var t = new TextItem();
            t.Text = s; t.Font = f; t.At = at; t.AtPoint = true; t.Color = c; t.Back = back;
            texts.Add(t);
        }

        void TextLater(string s, Font f, Rectangle r, Color c, Color back, TextFormatFlags flags)
        {
            var t = new TextItem();
            t.Text = s; t.Font = f; t.Rect = r; t.Color = c; t.Back = back; t.Flags = flags;
            texts.Add(t);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int DrawTextExW(IntPtr hdc, string text, int len, ref GRect rc, uint fmt, ref DrawTextParams p);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr GetCurrentObject(IntPtr hdc, uint type);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int c);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int m);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct GRect { public int L, T, R, B; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct DrawTextParams { public int Size, TabLength, LeftMargin, RightMargin, LengthDrawn; }

        struct TextKey : IEquatable<TextKey>
        {
            public string Text; public Font Font; public int Fg, Bg, Flags;
            public bool Equals(TextKey o) { return Fg == o.Fg && Bg == o.Bg && Flags == o.Flags && Font == o.Font && Text == o.Text; }
            public override bool Equals(object o) { return o is TextKey && Equals((TextKey)o); }
            public override int GetHashCode() { return Text.GetHashCode() ^ (Fg * 31) ^ (Bg * 977) ^ (Flags * 7919) ^ Font.GetHashCode(); }
        }
        sealed class TextBlit { public IntPtr Bmp; public int W, H, InkX, InkY, InkW, InkH; }

        readonly Dictionary<TextKey, TextBlit> textCache = new Dictionary<TextKey, TextBlit>();
        readonly Dictionary<Font, IntPtr> hfonts = new Dictionary<Font, IntPtr>();
        IntPtr memDc, memStock, memSel;

        const uint DT_CENTER = 1, DT_RIGHT = 2, DT_VCENTER = 4, DT_BOTTOM = 8, DT_CALCRECT = 0x400,
                   DT_ELLIPSES = 0x4000 | 0x8000 | 0x40000, DT_ALIGN = DT_CENTER | DT_RIGHT | DT_VCENTER | DT_BOTTOM;
        const uint SRCCOPY = 0x00CC0020;

        IntPtr HFont(Font f)
        {
            IntPtr h;
            if (!hfonts.TryGetValue(f, out h)) hfonts[f] = h = f.ToHfont();
            return h;
        }

        void EnsureMem()
        {
            if (memDc != IntPtr.Zero) return;
            memDc = CreateCompatibleDC(IntPtr.Zero);
            memStock = memSel = GetCurrentObject(memDc, 7);      // OBJ_BITMAP
        }

        // Every rendered string goes; fonts stay
        void DropBlits()
        {
            if (memDc != IntPtr.Zero) { SelectObject(memDc, memStock); memSel = memStock; }
            foreach (TextBlit b in textCache.Values) DeleteObject(b.Bmp);
            textCache.Clear();
        }

        // Fonts changing (scale), or the control going away
        void DropText()
        {
            DropBlits();
            foreach (IntPtr h in hfonts.Values) DeleteObject(h);
            hfonts.Clear();
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        static DrawTextParams Margins(TextItem t)
        {
            var p = new DrawTextParams();
            p.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DrawTextParams));
            if ((t.Flags & TextFormatFlags.NoPadding) == 0)
            {
                double over = t.Font.Height / 6.0;
                p.LeftMargin = (int)Math.Ceiling(over);
                p.RightMargin = (int)Math.Ceiling(over * 1.5);
            }
            return p;
        }

        TextBlit Blit(TextItem t)
        {
            TextKey k;
            k.Text = t.Text; k.Font = t.Font; k.Fg = t.Color.ToArgb(); k.Bg = t.Back.ToArgb(); k.Flags = (int)t.Flags;
            TextBlit b;
            if (textCache.TryGetValue(k, out b)) return b;
            if (textCache.Count >= 800) DropBlits();   // a running counter makes a new string a second

            EnsureMem();
            DrawTextParams dtp = Margins(t);
            uint fmt = (uint)t.Flags & 0x00FFFFFF & ~(DT_ALIGN | DT_ELLIPSES);
            IntPtr oldFont = SelectObject(memDc, HFont(t.Font));
            GRect rc; rc.L = 0; rc.T = 0; rc.R = 0; rc.B = 0;
            DrawTextExW(memDc, t.Text, -1, ref rc, fmt | DT_CALCRECT, ref dtp);
            int w = rc.R, h = rc.B;
            if (w <= 0 || h <= 0) { SelectObject(memDc, oldFont); return null; }

            b = new TextBlit(); b.W = w; b.H = h;
            using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (Graphics gg = Graphics.FromImage(bmp)) gg.Clear(t.Back);
                b.Bmp = bmp.GetHbitmap();
            }
            SelectObject(memDc, b.Bmp);
            SetBkMode(memDc, 1);                                // TRANSPARENT
            SetTextColor(memDc, ColorRef(t.Color));
            rc.R = w; rc.B = h;
            DrawTextExW(memDc, t.Text, -1, ref rc, fmt, ref dtp);
            SelectObject(memDc, memSel);
            SelectObject(memDc, oldFont);
            // Only the ink is ever blitted, so the bitmap's flat background
            // can't land on a pixel the in-place draw would have left alone
            // (a box corner under the left margin, say)
            using (Bitmap img = Image.FromHbitmap(b.Bmp))
            {
                var data = img.LockBits(new Rectangle(0, 0, w, h),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                int bg = t.Back.ToArgb() & 0xFFFFFF;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                        if ((System.Runtime.InteropServices.Marshal.ReadInt32(data.Scan0, yy * data.Stride + xx * 4) & 0xFFFFFF) != bg)
                        {
                            if (xx < x0) x0 = xx;
                            if (xx > x1) x1 = xx;
                            if (yy < y0) y0 = yy;
                            if (yy > y1) y1 = yy;
                        }
                img.UnlockBits(data);
                if (x1 >= 0) { b.InkX = x0; b.InkY = y0; b.InkW = x1 - x0 + 1; b.InkH = y1 - y0 + 1; }
            }
            textCache[k] = b;
            return b;
        }

        // What TextRenderer does when the string needs clipping or an ellipsis:
        // it measures, centres by hand, and draws top-aligned
        void DrawDirect(IntPtr hdc, TextItem t, int lineHeight)
        {
            DrawTextParams dtp = Margins(t);
            uint fmt = (uint)t.Flags & 0x00FFFFFF;
            IntPtr oldFont = SelectObject(hdc, HFont(t.Font));
            if (lineHeight <= 0)                    // not from the cache: measure here
            {
                GRect m; m.L = 0; m.T = 0; m.R = 0; m.B = 0;
                DrawTextExW(hdc, t.Text, -1, ref m, (fmt & ~(DT_ALIGN | DT_ELLIPSES)) | DT_CALCRECT, ref dtp);
                lineHeight = m.B;
            }
            GRect rc; rc.L = t.Rect.X; rc.T = t.Rect.Y; rc.R = t.Rect.Right; rc.B = t.Rect.Bottom;
            if (lineHeight <= t.Rect.Height)
            {
                if ((t.Flags & TextFormatFlags.VerticalCenter) != 0) rc.T += t.Rect.Height / 2 - lineHeight / 2;
                else if ((t.Flags & TextFormatFlags.Bottom) != 0) rc.T += t.Rect.Height - lineHeight;
            }
            fmt &= ~(DT_VCENTER | DT_BOTTOM);
            SetBkMode(hdc, 1);
            SetTextColor(hdc, ColorRef(t.Color));
            DrawTextExW(hdc, t.Text, -1, ref rc, fmt, ref dtp);
            SelectObject(hdc, oldFont);
        }

        void FlushText(Graphics g)
        {
            if (texts.Count == 0) return;
            IntPtr hdc = g.GetHdc();
            try
            {
                for (int i = 0; i < texts.Count; i++)
                {
                    TextItem t = texts[i];
                    if (t.Direct) { DrawDirect(hdc, t, 0); continue; }
                    TextBlit b = Blit(t);
                    if (b == null || b.InkW == 0) continue;
                    int x, y;
                    if (t.AtPoint) { x = t.At.X; y = t.At.Y; }
                    else
                    {
                        Rectangle r = t.Rect;
                        if (b.W > r.Width || b.H > r.Height) { DrawDirect(hdc, t, b.H); continue; }
                        x = (t.Flags & TextFormatFlags.Right) != 0 ? r.Right - b.W
                          : (t.Flags & TextFormatFlags.HorizontalCenter) != 0 ? (r.Left + r.Right - b.W) / 2
                          : r.X;
                        y = (t.Flags & TextFormatFlags.VerticalCenter) != 0 ? r.Y + r.Height / 2 - b.H / 2
                          : (t.Flags & TextFormatFlags.Bottom) != 0 ? r.Bottom - b.H
                          : r.Y;
                    }
                    if (b.Bmp != memSel) { SelectObject(memDc, b.Bmp); memSel = b.Bmp; }
                    BitBlt(hdc, x + b.InkX, y + b.InkY, b.InkW, b.InkH, memDc, b.InkX, b.InkY, SRCCOPY);
                }
            }
            finally { g.ReleaseHdc(hdc); }
            texts.Clear();
        }

        void DrawCard(Graphics g, Card c, int index)
        {
            SlotConfig s = c.Cfg;
            var r = c.Bounds; r.Offset(0, -scrollY);
            int dy = -scrollY;
            // A locked card's fields and buttons drop their tint along with the
            // fill - only the lock itself keeps the color, pointing the way back
            string tint = s.HotkeyOff ? "" : s.Color;
            // Text follows suit: dimmed on a locked card, the tint's deep hue
            // on a colored one, neutral otherwise
            Color labelCol = s.HotkeyOff ? Theme.DimText
                           : tint.Length > 0 ? Theme.TintChip(tint).Text : Muted;
            Color valueCol = s.HotkeyOff ? Theme.DimText
                           : tint.Length > 0 ? Theme.TintChip(tint).Text : Theme.FieldText;
            // In-card buttons: full tint when live, a desaturated whisper of it
            // when locked - still recognizably the card's color, clearly asleep
            Theme.ChipStyle cardChip = s.HotkeyOff ? Theme.TintChipDim(s.Color)
                                                   : Theme.TintChip(s.Color);

            bool lifted = index == dragIndex && dragMoved;
            // A locked card mutes its fill toward gray - still its color, but
            // drained - with a dashed border saying "off" at a glance
            Color fill = s.HotkeyOff ? Theme.LockedCardFill(s.Color) : Theme.CardFill(s.Color);
            var body = new Rectangle(r.X, r.Y + S(8), r.Width - 1, CardHeight(s) - S(9));
            if (!lifted && !s.HotkeyOff)
                Theme.RoundedBox(g, body, S(6), fill, CardLine);
            else
            {
                // The two decorated borders - thick accent while dragged,
                // dashed while locked - keep the full path
                using (var path = Theme.RoundPath(body, S(6)))
                {
                    var prev = g.SmoothingMode;
                    g.SmoothingMode = Glyphs.Mode;
                    g.FillPath(Theme.Brush(fill), path);
                    using (var p = new Pen(lifted ? Theme.Accent : CardLine, lifted ? 2f : 1f))
                    {
                        if (s.HotkeyOff && !lifted) p.DashStyle = DashStyle.Dash;
                        g.DrawPath(p, path);
                    }
                    g.SmoothingMode = prev;
                }
            }

            foreach (DrawnLabel dl in c.Labels)
                TextLater(dl.Text, labelFont, new Point(dl.At.X, dl.At.Y + dy), labelCol, fill);

            foreach (KeyValuePair<El, Rectangle> kv in c.Hit)
            {
                var er = kv.Value; er.Offset(0, dy);
                bool hot = index == hotIndex && kv.Key == hotEl;
                bool editing = index == editorIndex && kv.Key == editorEl;
                if (editing) continue;      // the live control is sitting there

                switch (kv.Key)
                {
                    case El.Name:
                        // The name keeps the card's color even locked - in the
                        // drained shade the locked chips wear - so a rolled-up
                        // or locked card is still identifiable at a glance
                        bool nameTinted = Theme.TintOf(s.Color).Name.Length > 0;
                        Color nameFore = s.Name.Length == 0 ? SystemColors.GrayText
                                       : s.HotkeyOff && nameTinted ? Theme.TintChipDim(s.Color).Text
                                       : valueCol;
                        DrawField(g, er, s.Name.Length > 0 ? s.Name : "Auto-Clicker #" + (index + 1),
                                  nameFore, s.Color, s.HotkeyOff);
                        break;
                    // Edge buttons wear their meaning; buttons inside the card
                    // wear the card's tint
                    case El.Gear: Chip(g, er, "gear", hot,cardChip); break;
                    case El.Rec:
                        if (c.Armed)
                        {
                            // Armed wears the card's own color at full volume -
                            // the saturated swatch tone; accent on plain cards
                            Color on = Theme.TintOf(s.Color).Name.Length > 0
                                     ? Theme.SwatchOf(s.Color) : Theme.Accent;
                            Box(g, new Rectangle(er.X, er.Y, er.Width - 1, er.Height - 1), S(4), on, on);
                            Later(g, delegate(Graphics gg) { Glyphs.Draw(gg, er, "rec", Color.White, on); });
                        }
                        else Chip(g, er, "rec", hot,cardChip);
                        break;
                    case El.Lock: Chip(g, er, s.HotkeyOff ? "lock" : "unlock", hot,cardChip); break;
                    case El.Remove: Chip(g, er, "x", hot,cardChip); break;
                    case El.Dup: Chip(g, er, "copy", hot,cardChip); break;
                    case El.EditMacro: Chip(g, er, "timeline", hot,cardChip); break;
                    case El.DelMacro: Chip(g, er, "trash", hot,cardChip); break;
                    case El.Color: DrawSwatch(g, er, s.Color, hot, cardChip, s.HotkeyOff); break;
                    // Collapse folds things away (bronze); expand brings them
                    // back (green - the go color)
                    case El.Min: Chip(g, er, s.Collapsed ? "plus" : "minus", hot, cardChip); break;
                    case El.Grip: DrawGrip(g, er, hot || index == dragIndex); break;
                    case El.Pos: Chip(g, er, s.IsFixed ? "target" : "mouse", hot,cardChip); break;
                    case El.Pick: Chip(g, er, "pin", hot,cardChip); break;
                    case El.Preview:
                        // Deliberately quiet: a bare muted glyph at rest, the
                        // chip appearing only under the pointer
                        if (hot) Chip(g, er, "eye", true, cardChip);
                        else
                        {
                            Color eye = Theme.TintOf(s.Color).Name.Length > 0
                                      ? Theme.TintChipDim(s.Color).Text : Theme.MutedText;
                            Later(g, delegate(Graphics gg) { Glyphs.Draw(gg, er, "eye", eye, Color.Transparent); });
                        }
                        break;
                    case El.Hotkey:
                        DrawField(g, er, s.Hotkey.Trim().Length > 0
                                  ? HotkeyParser.Parse(s.Hotkey).ToString() : "None",
                                  valueCol, s.Color, s.HotkeyOff);
                        break;
                    case El.Interval:
                        // Held down: the interval doesn't apply, and the
                        // grayed field says so instead of showing a number
                        // that means nothing
                        DrawField(g, er, s.HoldDown ? "held"
                                  : s.Interval.ToString(CultureInfo.InvariantCulture),
                                  valueCol, s.Color, s.HotkeyOff || s.HoldDown);
                        break;
                    case El.HoldTgl:
                        Chip(g, er, s.HoldDown ? "toggle-on" : "toggle-off",
                                 hot, cardChip);
                        break;
                    case El.Loop:
                        Chip(g, er, s.MacroLoop ? "loop" : "once", hot, cardChip);
                        break;
                    case El.Speed:
                        DrawField(g, er, s.MacroSpeed.ToString(CultureInfo.InvariantCulture),
                                  valueCol, s.Color, s.HotkeyOff);
                        break;
                    case El.Input: DrawCombo(g, er, s.Input, hot, s.Color, valueCol, s.HotkeyOff); break;
                    case El.XBox:
                        DrawField(g, er, s.X.ToString(CultureInfo.InvariantCulture), valueCol, s.Color, s.HotkeyOff);
                        break;
                    case El.YBox:
                        DrawField(g, er, s.Y.ToString(CultureInfo.InvariantCulture), valueCol, s.Color, s.HotkeyOff);
                        break;
                    case El.MacroSel: DrawCombo(g, er, MacroFile.Display(s.Macro), hot, s.Color, valueCol, s.HotkeyOff); break;
                    case El.Key: DrawField(g, er, s.CustomKey, valueCol, s.Color, s.HotkeyOff); break;
                    case El.None:
                        TextNow(c.Status, c.StatusBold ? boldFont : Font,
                            Optical(er), c.StatusColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                          | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        break;
                }
            }
        }


        // Tinted cards tint their fields the same way as their buttons: a step
        // lighter than the card in light mode, a step darker in dark mode.
        // GDI centres text on the font line box, and that box reserves descender
        // room the text here rarely uses - numbers, "F6", "Left Click". Measured
        // for the UI font at 9.75pt: the glyphs land 0.83 px below the middle of
        // the box, which reads as text sitting low in every field. Optical
        // centring is the box shifted up one.
        static Rectangle Optical(Rectangle r) { r.Offset(0, -1); return r; }

        void DrawField(Graphics g, Rectangle r, string text, Color fore, string tint, bool dimTint)
        {
            bool tinted = Theme.TintOf(tint).Name.Length > 0;
            Theme.ChipStyle cs = dimTint ? Theme.TintChipDim(tint) : Theme.TintChip(tint);
            Box(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), S(4),
                tinted ? cs.Back : Theme.FieldBack, tinted ? cs.Line : Theme.FieldLine);
            if (fore == SystemColors.WindowText) fore = Theme.FieldText;
            else if (Theme.Dark && fore == SystemColors.GrayText) fore = Theme.OffText;
            TextLater(text, Font, Optical(Rectangle.Inflate(r, -S(4), -1)), fore,
                tinted ? cs.Back : Theme.FieldBack,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
              | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void DrawCombo(Graphics g, Rectangle r, string text, bool hot, string tint, Color fore, bool dim)
        {
            bool tinted = Theme.TintOf(tint).Name.Length > 0;
            Theme.ChipStyle cs = dim ? Theme.TintChipDim(tint) : Theme.TintChip(tint);
            Color back = tinted ? (hot ? cs.Hot : cs.Back) : (hot ? Theme.ChipHot : Theme.FieldBack);
            Box(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), S(4),
                back, tinted ? cs.Line : Theme.FieldLine);
            // A font glyph here sat below center and changed with the font
            Color chev = tinted ? cs.Text : Theme.MutedText;
            Later(g, delegate(Graphics gg)
            {
                Glyphs.Chevron(gg, r.Right - S(12), r.Y + r.Height / 2f - Theme.Sf(1.5f), chev);
            });
            TextLater(text, Font,
                Optical(new Rectangle(r.X + S(4), r.Y, Math.Max(4, r.Width - S(26)), r.Height)),
                fore, back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
              | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Six dots that say "you can drag this"
        void DrawGrip(Graphics g, Rectangle r, bool hot)
        {
            SolidBrush b = Theme.Brush(hot ? Theme.ChipText : Theme.ChipLine);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 2; col++)
                    g.FillRectangle(b, r.X + r.Width / 2 - S(4) + col * S(5),
                                       r.Y + r.Height / 2 - S(6) + row * S(5), S(2), S(2));
        }

        // The color button: a dot of the card's tint, or a hollow ring for none
        void DrawSwatch(Graphics g, Rectangle r, string tint, bool hot,
                        Theme.ChipStyle cs, bool locked)
        {
            Box(g, r, S(4), hot ? cs.Hot : cs.Back, cs.Line);
            int d = S(11);
            var dot = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            bool filled = Theme.TintOf(tint).Name.Length > 0;
            Color dotCol = locked ? Theme.SwatchOfDim(tint) : Theme.SwatchOf(tint);
            Color ringCol = locked ? Theme.SwatchOfDim("") : Theme.SwatchOf("");
            Later(g, delegate(Graphics gg)
            {
                var prev = gg.SmoothingMode;
                gg.SmoothingMode = Glyphs.Mode;
                if (filled) gg.FillEllipse(Theme.Brush(dotCol), dot);
                else gg.DrawEllipse(Theme.Pen(ringCol, Theme.Sf(1.6f)), dot);
                gg.SmoothingMode = prev;
            });
        }

        // --- hit testing and hover --------------------------------------------

        bool HitTest(Point view, out int index, out El el)
        {
            var pt = new Point(view.X, view.Y + scrollY);
            for (int i = 0; i < cards.Count; i++)
            {
                Card c = cards[i];
                var b = c.Bounds; b.Inflate(0, S(12));   // dup straddles the border
                if (!b.Contains(pt)) continue;
                foreach (KeyValuePair<El, Rectangle> kv in c.Hit)
                {
                    if (kv.Key == El.None) continue;
                    if (kv.Value.Contains(pt)) { index = i; el = kv.Key; return true; }
                }
                index = i; el = El.None; return true;
            }
            index = -1; el = El.None; return false;
        }

        static bool IsButton(El el)
        {
            return el == El.Gear || el == El.Rec || el == El.Lock || el == El.Remove
                || el == El.Dup || el == El.Pick || el == El.DelMacro
                || el == El.EditMacro || el == El.Color || el == El.Pos || el == El.Min
                || el == El.Preview || el == El.Loop || el == El.HoldTgl;
        }

        string TipFor(El el, int index)
        {
            switch (el)
            {
                case El.Gear: return "Advanced options";
                case El.Rec: return "Ready this card, then record with the record hotkey";
                case El.Lock: return "Enable / disable this hotkey";
                case El.Remove: return "Remove clicker";
                case El.Dup: return "Duplicate clicker";
                case El.EditMacro: return "Edit macro - or build one from scratch";
                case El.DelMacro: return "Delete macro";
                case El.Color: return "Card color";
                case El.Pick: return "Pick the spot on screen";
                case El.Preview: return "Show where it clicks";
                case El.Grip: return "Drag to reorder";
                case El.Min:
                    if (index < 0 || index >= cards.Count) return null;
                    return cards[index].Cfg.Collapsed ? "Expand" : "Collapse to one line";
                case El.Pos:
                    if (index < 0 || index >= cards.Count) return null;
                    return cards[index].Cfg.IsFixed
                        ? "Clicks a fixed spot - click to follow the mouse instead"
                        : "Clicks wherever the mouse is - click to use a fixed spot";
                case El.Loop:
                    if (index < 0 || index >= cards.Count) return null;
                    return cards[index].Cfg.MacroLoop
                        ? "Repeats until stopped - click to play once per press"
                        : "Plays once per press - click to repeat until stopped";
                case El.HoldTgl:
                    if (index < 0 || index >= cards.Count) return null;
                    return cards[index].Cfg.HoldDown
                        ? "Held down until stopped"
                        : "Hold down instead of clicking";
                default: return null;
            }
        }

        // Tooltips wait half a beat before appearing - a cursor passing through
        // shouldn't leave a wake of labels - and vanish the moment the mouse
        // moves off the thing they describe.
        Timer tipTimer;
        string tipPending;
        Point tipAt;

        void QueueTip(string tip, Point at)
        {
            if (tipTimer == null)
            {
                tipTimer = new Timer();
                tipTimer.Interval = 550;
                tipTimer.Tick += delegate
                {
                    tipTimer.Stop();
                    if (tipPending != null)
                        tips.Show(tipPending, this, tipAt.X + S(14), tipAt.Y + S(22), 4000);
                };
            }
            tipTimer.Stop();
            tipPending = tip; tipAt = at;
            if (tip != null) tipTimer.Start();
        }

        void HideTip()
        {
            if (tipTimer != null) tipTimer.Stop();
            tipPending = null;
            // Hide alone doesn't reliably dismiss a duration-based Show; the
            // Active bounce tears the balloon down for real
            tips.Hide(this);
            tips.Active = false;
            tips.Active = true;
        }

        // --- drag to reorder --------------------------------------------------

        int dragIndex = -1;             // card being dragged, -1 = none
        bool dragMoved;                 // a real drag, not a stray click
        Point dragDownAt;
        public event Action<int, int> Reordered;   // (from, to), already applied here

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Mid-drag the pointer's only job is choosing the card's new home.
            // Reordering live as it crosses other cards - no ghost, no drop
            // marker, the list simply follows the hand.
            if (dragIndex >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                if (!dragMoved && (Math.Abs(e.X - dragDownAt.X) > 3 || Math.Abs(e.Y - dragDownAt.Y) > 3))
                    dragMoved = true;
                if (!dragMoved) return;
                var pt = new Point(e.X, e.Y + scrollY);
                for (int k = 0; k < cards.Count; k++)
                {
                    if (k == dragIndex) continue;
                    var b = cards[k].Bounds; b.Inflate(S(3), RowSpace / 2);
                    if (!b.Contains(pt)) continue;
                    Card moving = cards[dragIndex];
                    cards.RemoveAt(dragIndex);
                    cards.Insert(k, moving);
                    int from = dragIndex; dragIndex = k;
                    Reflow();
                    if (Reordered != null) Reordered(from, k);
                    break;
                }
                return;
            }

            if (barDrag) { BarScrollTo(e.Y, barGrabDy); return; }
            bool overBar = e.X >= ClientSize.Width - BarGutter;
            if (overBar != barHot)
            {
                barHot = overBar;
                Invalidate(BarTrack());
            }
            if (overBar)
            {
                if (hotIndex >= 0) { int oi = hotIndex; hotIndex = -1; hotEl = El.None; InvalidateCard(oi); }
                Cursor = Cursors.Default;
                HideTip();
                return;
            }

            int i; El el;
            HitTest(e.Location, out i, out el);
            if (!IsButton(el) && el != El.Hotkey && el != El.Grip)
                el = IsEditable(el) ? el : El.None;
            if (i != hotIndex || el != hotEl)
            {
                int oi = hotIndex; hotIndex = i; hotEl = el;
                if (oi >= 0 && oi < cards.Count) InvalidateCard(oi);
                if (i >= 0) InvalidateCard(i);

                Cursor = el == El.Grip ? Cursors.SizeAll
                       : IsButton(el) ? Theme.Hand
                       : IsEditable(el) ? Theme.IBeam : Cursors.Default;

                HideTip();
                string tip = TipFor(el, i);
                if (tip != null) QueueTip(tip, e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (barDrag) { barDrag = false; Invalidate(BarTrack()); return; }
            if (dragIndex >= 0)
            {
                int idx = dragIndex;
                dragIndex = -1; dragMoved = false;
                if (idx < cards.Count) InvalidateCard(idx);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotIndex >= 0) InvalidateCard(hotIndex);
            hotIndex = -1; hotEl = El.None;
            if (barHot) { barHot = false; Invalidate(BarTrack()); }
            HideTip();
        }

        static bool IsEditable(El el)
        {
            return el == El.Name || el == El.Hotkey || el == El.Interval
                || el == El.Input || el == El.XBox || el == El.YBox
                || el == El.MacroSel || el == El.Key || el == El.Speed;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;

            if (e.X >= ClientSize.Width - BarGutter)
            {
                EndEdit(true);
                Rectangle thumb = BarThumb();
                if (thumb.IsEmpty) return;
                if (thumb.Contains(e.Location))
                {
                    barDrag = true;
                    barGrabDy = e.Y - thumb.Y;      // the thumb keeps its grip point
                }
                else
                {
                    // page toward the click, the way a track click behaves
                    ScrollTo(scrollY + (e.Y < thumb.Y ? -1 : 1) * ClientSize.Height);
                }
                Invalidate(BarTrack());
                return;
            }

            int i; El el;
            if (!HitTest(e.Location, out i, out el)) { EndEdit(true); return; }

            if (el == El.Grip)
            {
                EndEdit(true);
                dragIndex = i;
                dragMoved = false;
                dragDownAt = e.Location;
                InvalidateCard(i);
                return;
            }

            if (IsButton(el))
            {
                EndEdit(true);
                switch (el)
                {
                    case El.Min:
                        cards[i].Cfg.Collapsed = !cards[i].Cfg.Collapsed;
                        Reflow();
                        Fire(Changed, i);
                        break;
                    case El.Gear: Fire(OptionsClicked, i); break;
                    case El.Rec: Fire(RecordClicked, i); break;
                    case El.Remove: Fire(RemoveClicked, i); break;
                    case El.Dup: Fire(DuplicateClicked, i); break;
                    case El.Pick: Fire(PickPosClicked, i); break;
                    case El.Preview: Fire(PreviewClicked, i); break;
                    case El.EditMacro: Fire(EditMacroClicked, i); break;
                    case El.DelMacro: Fire(DeleteMacroClicked, i); break;
                    case El.Color: Fire(ColorClicked, i); break;
                    case El.Pos:
                        // The two-state toggle: reticle <-> mouse
                        cards[i].Cfg.PosMode = cards[i].Cfg.IsFixed
                            ? "Current Position" : "Fixed Position";
                        Reflow();               // the X/Y row appears or goes
                        Fire(Changed, i);
                        break;
                    case El.Loop:
                        // Repeat <-> play once; a running card picks it up on
                        // its next start, like every other snapshotted setting
                        cards[i].Cfg.MacroLoop = !cards[i].Cfg.MacroLoop;
                        InvalidateCard(i);
                        Fire(Changed, i);
                        break;
                    case El.HoldTgl:
                        cards[i].Cfg.HoldDown = !cards[i].Cfg.HoldDown;
                        InvalidateCard(i);
                        Fire(Changed, i);
                        break;
                    case El.Lock:
                        cards[i].Cfg.HotkeyOff = !cards[i].Cfg.HotkeyOff;
                        RefreshStatus(i, false, 0);
                        InvalidateCard(i);
                        Fire(Changed, i);
                        break;
                }
                return;
            }

            // A held-down card has no interval to edit - the field is a label
            if (el == El.Interval && cards[i].Cfg.HoldDown)
            {
                Notice(i, "Held down the whole run - the interval doesn't apply.");
                EndEdit(true);
                return;
            }
            if (IsEditable(el)) BeginEdit(i, el);
            else EndEdit(true);
        }

        void Fire(Action<int> h, int i) { if (h != null) h(i); }

        // --- the one live editor ----------------------------------------------

        public void EndEdit(bool commit)
        {
            if (editor == null) return;
            Control ed = editor;
            int i = editorIndex; El el = editorEl;
            editor = null; editorIndex = -1; editorEl = El.None;

            if (!commit && i >= 0 && i < cards.Count
                && (el == El.Name || el == El.Interval || el == El.XBox
                 || el == El.YBox || el == El.Key || el == El.Speed))
                CommitText(i, el, editorOriginal);       // put the old value back

            Controls.Remove(ed);
            ed.Dispose();
            if (i >= 0 && i < cards.Count)
            {
                RefreshStatus(i, false, 0);
                Reflow();       // field widths follow content
            }
        }

        void BeginEdit(int i, El el)
        {
            EndEdit(true);
            Card c = cards[i];
            SlotConfig s = c.Cfg;
            Rectangle r = c.Hit[el]; r.Offset(0, -scrollY);
            editorIndex = i; editorEl = el;
            // The live editor sits over a drawn field, so it wears the same
            // tint the field does - anything else flashes white on a colored card
            bool tintedCard = Theme.TintOf(s.Color).Name.Length > 0;
            Color editorBack = tintedCard
                             ? (s.HotkeyOff ? Theme.TintChipDim(s.Color).Back : Theme.TintChip(s.Color).Back)
                             : (Theme.Dark ? Theme.FieldBack : Color.White);
            // The rounded editors paint their corners in what's behind them -
            // here, the card's own fill
            string bdTint = s.Color; bool bdLocked = s.HotkeyOff;
            Func<Color> behind = delegate
            {
                return bdLocked ? Theme.LockedCardFill(bdTint) : Theme.CardFill(bdTint);
            };

            switch (el)
            {
                case El.Input:
                case El.MacroSel:
                {
                    // No editor materializes at all: the drawn popup opens over
                    // the drawn combo. The card object (not its index) rides
                    // the closure, so a reorder mid-popup can't misfile the pick.
                    editorIndex = -1; editorEl = El.None;
                    var list = new List<string>();
                    if (el == El.Input)
                        list.AddRange(new string[] { "Left Click", "Right Click", "Middle Click",
                                                     "X1 Button", "X2 Button", "Custom Key", "Macro" });
                    else
                    {
                        // Rescan the folder first: a take copied in by hand
                        // is in the list the moment it's asked for
                        if (MacroListOpening != null) MacroListOpening();
                        list.AddRange(macroNames);
                        // a selection whose file is missing stays selectable
                        if (s.Macro.Length > 0 && !list.Contains(s.Macro))
                            list.Add(s.Macro);
                    }
                    string cur = el == El.Input ? s.Input : s.Macro;
                    Card card = c;
                    bool isInput = el == El.Input;
                    // Takes are listed without their extension; the pick maps back
                    var shown = new List<string>(list.Count);
                    foreach (string n in list) shown.Add(isInput ? n : MacroFile.Display(n));
                    DropList.Open(this, RectangleToScreen(r), shown,
                                  isInput ? cur : MacroFile.Display(cur), delegate(string v)
                    {
                        int k = shown.IndexOf(v);
                        if (k >= 0) v = list[k];
                        if (isInput) s.Input = v; else s.Macro = v;
                        // A macro's loop gap may be 0; a clicker's interval
                        // must not be - switching the input type would
                        // otherwise carry the 0 over as a 10k cps request
                        if (isInput && v != "Macro" && s.Interval < 1) s.Interval = 50;
                        int idx = cards.IndexOf(card);
                        if (idx >= 0) Fire(Changed, idx);
                        Reflow();
                    });
                    return;
                }

                // The custom key records a keypress like the hotkey field
                // does - it was a text box once, which looked nothing like
                // its sibling and couldn't say "Shift" at all
                case El.Hotkey:
                case El.Key:
                {
                    var hk = new HotkeyBox();
                    hk.SingleKey = el == El.Key;
                    hk.Backdrop = behind;
                    hk.BackColor = editorBack;
                    hk.ForeColor = Theme.FieldText;
                    hk.Spec = el == El.Key ? s.CustomKey : s.Hotkey;
                    hk.SetBounds(r.X, r.Y, Math.Max(r.Width, 60), r.Height);
                    bool isKey = el == El.Key;
                    hk.ComboChanged += delegate
                    {
                        if (isKey) s.CustomKey = HotkeyParser.VkToName(hk.Combo.Vk);
                        else s.Hotkey = hk.Spec;
                        RefreshStatus(editorIndex, false, 0);
                        Fire(Changed, editorIndex);
                    };
                    // Posted: destroying a control from inside its own focus
                    // handler is asking for reentrancy trouble
                    hk.LostFocus += delegate
                    {
                        BeginInvoke((MethodInvoker)delegate { if (editor == hk) EndEdit(true); });
                    };
                    editor = hk;
                    Controls.Add(hk);
                    hk.Focus();
                    return;
                }

                default:
                {
                    TextBox box = (el == El.Interval || el == El.XBox || el == El.YBox
                                   || el == El.Speed)
                                ? new NumberBox { AllowNegative = el == El.XBox || el == El.YBox }
                                : new Field();
                    ((Field)box).Backdrop = behind;
                    box.BackColor = editorBack;
                    box.ForeColor = Theme.FieldText;
                    string cur =
                        el == El.Name ? s.Name :
                        el == El.Interval ? s.Interval.ToString(CultureInfo.InvariantCulture) :
                        el == El.Speed ? s.MacroSpeed.ToString(CultureInfo.InvariantCulture) :
                        el == El.XBox ? s.X.ToString(CultureInfo.InvariantCulture) :
                        s.Y.ToString(CultureInfo.InvariantCulture);
                    editorOriginal = cur;
                    box.Text = cur;
                    box.SetBounds(r.X, r.Y, Math.Max(r.Width, S(60)), r.Height);
                    box.TextChanged += delegate
                    {
                        if (editorIndex >= 0) CommitText(editorIndex, editorEl, box.Text);
                    };
                    box.KeyDown += delegate(object o, KeyEventArgs ke)
                    {
                        if (ke.KeyCode == Keys.Enter) { ke.SuppressKeyPress = true; EndEdit(true); }
                        else if (ke.KeyCode == Keys.Escape) { ke.SuppressKeyPress = true; EndEdit(false); }
                    };
                    box.LostFocus += delegate
                    {
                        BeginInvoke((MethodInvoker)delegate { if (editor == box) EndEdit(true); });
                    };
                    editor = box;
                    Controls.Add(box);
                    box.Focus();
                    box.SelectAll();
                    return;
                }
            }
        }

        void CommitText(int i, El el, string text)
        {
            SlotConfig s = cards[i].Cfg;
            int v;
            switch (el)
            {
                case El.Name: s.Name = text; break;
                // 50, not 1: an unparseable box is someone mid-edit, and a 1 ms
                // fallback is a thousand clicks a second
                case El.Interval: s.Interval = int.TryParse(text, out v) && v > 0 ? v : 50; break;
                // 100 fallback for the same mid-edit reason as the interval's
                case El.Speed:
                    s.MacroSpeed = int.TryParse(text, out v) && v > 0
                        ? Math.Max(SlotConfig.MacroSpeedMin, Math.Min(SlotConfig.MacroSpeedMax, v))
                        : 100;
                    break;
                case El.XBox: s.X = int.TryParse(text, out v) ? v : 0; break;
                case El.YBox: s.Y = int.TryParse(text, out v) ? v : 0; break;
            }
            Fire(Changed, i);
        }

        // The input type changed under the editor (a recording landed, a
        // profile loaded): a stale editor would write into the wrong field
        public void CancelEdit() { EndEdit(false); }

        // Dark toggled: stored status colors are the old mood's, so recompute
        public void ThemeChanged()
        {
            EndEdit(true);
            BackColor = Theme.FormBack;
            Theme.StyleScrollbars(this);
            for (int i = 0; i < cards.Count; i++) RefreshStatus(i, false, 0);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleScrollbars(this);
        }

        // --- scrolling ----------------------------------------------------------

        // The bar is drawn here rather than left to the window: the native one
        // arrives with Windows own metrics and arrow buttons, against a surface
        // where every other control is a flat rounded shape. Its width is
        // reserved whether or not there is anything to scroll, so the cards
        // never shift sideways when the list outgrows the window.
        static int BarW { get { return S(6); } }
        static int BarGutter { get { return S(12); } }
        static int BarPad { get { return S(3); } }
        bool barHot, barDrag;
        int barGrabDy;

        // The card area: everything left of the bar gutter.
        int ViewW { get { return Math.Max(MinWidth, ClientSize.Width - BarGutter); } }

        void UpdateScrollInfo()
        {
            int max = Math.Max(0, ContentHeight() - ClientSize.Height);
            if (scrollY > max) scrollY = max;
            if (scrollY < 0) scrollY = 0;
        }

        Rectangle BarTrack()
        {
            return new Rectangle(ClientSize.Width - BarGutter + (BarGutter - BarW) / 2,
                                 BarPad, BarW, Math.Max(0, ClientSize.Height - BarPad * 2));
        }

        // Empty when everything fits: the gutter stays reserved, nothing drawn.
        Rectangle BarThumb()
        {
            int content = ContentHeight(), view = ClientSize.Height;
            if (content <= view || view <= 0) return Rectangle.Empty;
            Rectangle t = BarTrack();
            int h = Math.Max(S(28), (int)((long)t.Height * view / content));
            if (h >= t.Height) return Rectangle.Empty;
            int span = t.Height - h, max = content - view;
            int y = max <= 0 ? 0 : (int)((long)span * scrollY / max);
            return new Rectangle(t.X, t.Y + y, t.Width, h);
        }

        void DrawBar(Graphics g)
        {
            Rectangle thumb = BarThumb();
            if (thumb.IsEmpty) return;
            var prev = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.RoundPath(thumb, BarW / 2))
                g.FillPath(Theme.Brush(barDrag || barHot ? Theme.ScrollThumbHot : Theme.ScrollThumb), path);
            g.SmoothingMode = prev;
        }

        void BarScrollTo(int mouseY, int grabDy)
        {
            Rectangle t = BarTrack();
            int h = BarThumb().Height;
            int span = Math.Max(1, t.Height - h);
            int max = Math.Max(0, ContentHeight() - ClientSize.Height);
            ScrollTo((int)((long)(mouseY - t.Y - grabDy) * max / span));
        }


        public void ScrollTo(int y)
        {
            int max = Math.Max(0, ContentHeight() - ClientSize.Height);
            y = Math.Max(0, Math.Min(max, y));
            if (y == scrollY) return;
            EndEdit(true);                  // the editor sits at view coords
            scrollY = y;
            UpdateScrollInfo();
            Invalidate();                   // cards and the thumb together
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollTo(scrollY - Math.Sign(e.Delta) * S(100));
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            EndEdit(true);
            int before = scrollY;
            UpdateScrollInfo();             // may clamp scrollY at the bottom
            if (ClientSize.Width != paintedWidth)
            {
                // A width DRAG reflows every card on every WM_SIZE tick. Every
                // tick paints - skipping frames reads as jitter - but paints
                // cheap (no antialiasing); the settle timer delivers one
                // full-quality pass when the mouse stops. A resize with no
                // button down - zoom, a DPI change, a restored window - is a
                // single event, and paints crisp at once.
                bool dragging = Interactive || (MouseButtons & MouseButtons.Left) != 0;
                Glyphs.Fast = dragging;
                settle.Stop();
                if (dragging) settle.Start();
                Invalidate();
            }
            else if (scrollY != before)
                Invalidate();
        }

        // Paging, now that no scrollbar window supplies it
        protected override bool ProcessCmdKey(ref Message msg, Keys k)
        {
            if (editor == null)
            {
                if (k == Keys.PageDown) { ScrollTo(scrollY + ClientSize.Height); return true; }
                if (k == Keys.PageUp)   { ScrollTo(scrollY - ClientSize.Height); return true; }
            }
            return base.ProcessCmdKey(ref msg, k);
        }

    }
}
