// ===========================================================================
//  Dialogs - Settings, per-card Advanced options, the window picker, and the
//  small text prompt used for naming profiles and macros
// ---------------------------------------------------------------------------
//  Everything opens centered on the owner (CenterParent) - a pop-up must
//  never land in the middle of another monitor.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Polyclicker
{
    // A dialog scaffold: owner-centered, fixed border, ESC cancels.
    class AppDialog : Form
    {
        public AppDialog(string title)
        {
            Text = title;
            Font = Theme.UIFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;                   // a sizable border would show the stock one
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape) return;
                if (EscapeCloses()) { DialogResult = DialogResult.Cancel; Close(); }
                e.Handled = e.SuppressKeyPress = true;   // never ding a text field
            };
        }

        // Escape closes the dialog - unless a subclass has something open
        // that Escape should dismiss first (an in-place editor, say)
        protected virtual bool EscapeCloses() { return true; }

        // Dialogs are laid out in 100% pixels, then scaled as a whole: real
        // controls follow their bounds, and the font they inherit is already
        // sized to match. Once only, whoever asks first - the load, or a
        // constructor that needs its final size before showing.
        bool scaled;
        protected void ApplyScale()
        {
            if (scaled) return;
            scaled = true;
            if (Math.Abs(Theme.Scale - 1f) > 0.001f)
                Scale(new SizeF(Theme.Scale, Theme.Scale));
        }

        // After every control exists, and before the user sees any of them
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyScale();
            // Windows centred the window on its owner when the handle was
            // created, at the unscaled size; now that it has its real size,
            // centre it again
            if (StartPosition == FormStartPosition.CenterParent) CenterToParent();
            Theme.Apply(this);
            Theme.DarkTitleBar(this);
        }

        // The dialog moved to a monitor with another scale (or the scale
        // changed under it): refont, rescale every control by the ratio, and
        // take the size Windows suggests. Subclasses with drawn parts that
        // cached a size override OnScaleChanged.
        const int WM_DPICHANGED = 0x02E0;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct DpiRect { public int L, T, R, B; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                int dpi = ((int)(long)m.WParam >> 16) & 0xFFFF;
                var r = (DpiRect)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(DpiRect));
                // This window's own change. The scale the app lays out with
                // belongs to the main window, which gets its own message when
                // the change is system-wide; setting it from here scaled the
                // main window's cards along with a dialog dragged elsewhere.
                float ratio = myDpi > 0 ? dpi / (float)myDpi : 1f;
                myDpi = dpi;
                RescaleBy(ratio, false);
                Bounds = new Rectangle(r.L, r.T, r.R - r.L, r.B - r.T);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        int myDpi;                      // this window's monitor, as of the last change

        // Where this window sits relative to Theme.Scale: 1 on the main
        // window's monitor, the DPI ratio once dragged to another. Drawn
        // parts that size themselves from Theme.S() multiply by it.
        protected float LocalScale = 1f;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            myDpi = Theme.WindowDpi(Handle);
        }

        // Theme.Scale already moved (zoom, or the main window's monitor):
        // bring this window along. Also how zoom reaches an open editor.
        public void RescaleBy(float ratio) { RescaleBy(ratio, true); }

        void RescaleBy(float ratio, bool themeMoved)
        {
            if (Math.Abs(ratio - 1f) < 0.001f) return;
            if (themeMoved)
            {
                Theme.ResetFonts();
                Font f = Theme.UIFont;
                Font = Math.Abs(LocalScale - 1f) < 0.001f ? f
                     : new Font(f.FontFamily, f.Size * LocalScale, f.Style, f.Unit);
            }
            else
            {
                LocalScale *= ratio;
                Font = new Font(Font.FontFamily, Font.Size * ratio, Font.Style, Font.Unit);
            }
            Scale(new SizeF(ratio, ratio));
            OnScaleChanged();
            Invalidate(true);
        }

        protected virtual void OnScaleChanged() { }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Theme.ZoomKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // The theme flipped while this dialog is open (the live dark toggle):
        // restyle everything, including any glyph images, in place
        public void ReTheme()
        {
            Theme.Apply(this);
            Theme.DarkTitleBar(this);
            Invalidate(true);
        }

        protected Label Muted(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = Color.Gray;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 1000);
            l.AutoSize = false;
            // +6, not +4: measuring is exact to the glyph box, and a wrapped
            // line's descenders sat right on the edge of the label.
            // Measured at the width the label will HAVE once the dialog is
            // scaled, with the (already scaled) font, then expressed back in
            // 100% units - so the wrap count survives ApplyScale.
            // A Label wraps a few px narrower than its width (its own padding),
            // so measure narrower too or the last line falls off at 150%
            int shown = TextRenderer.MeasureText(text, Font, new Size(Theme.S(w) - Theme.S(6), 1000),
                        TextFormatFlags.WordBreak).Height + Theme.S(6);
            l.Size = new Size(w, (int)Math.Ceiling(shown / Theme.Scale));
            Controls.Add(l);
            return l;
        }

        protected Label Plain(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = w == 0;
            if (w > 0) l.Size = new Size(w, 20);
            Controls.Add(l);
            return l;
        }

        // Every dialog button is a drawn chip - rounded in both themes, unlike
        // the stock renderer which only rounds in light mode
        protected ChipButton Btn(string text, int x, int y, int w, int h)
        {
            var b = new ChipButton();
            b.Kind = "";
            b.Text = text;
            b.Style = delegate { return Theme.NeutralChip; };
            b.SetBounds(x, y, w, h);
            Controls.Add(b);
            return b;
        }

        // The same chip with a glyph beside its label
        protected ChipButton IconBtn(string kind, string text, int x, int y, int w, int h)
        {
            ChipButton b = Btn(text, x, y, w, h);
            b.Kind = kind;
            return b;
        }
    }

    // --- the small "give it a name" prompt ---------------------------------
    sealed class TextPrompt : AppDialog
    {
        readonly Field box = new Field();

        public string Value { get { return box.Text.Trim(); } }

        public TextPrompt(string title, string caption, string prefill) : base(title)
        {
            ClientSize = new Size(340, 118);
            Plain(caption, 14, 12, 312);
            box.SetBounds(14, 40, 312, 25);
            box.Text = prefill ?? "";
            box.SelectAll();
            Controls.Add(box);

            ChipButton ok = Btn("OK", 154, 78, 82, 28);
            ChipButton cancel = Btn("Cancel", 244, 78, 82, 28);
            ok.Style = delegate { return Theme.GoChip; };
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    // --- a yes/no question ---------------------------------------------------
    // The stock MessageBox is a system dialog: unthemed in dark mode, and it
    // positions itself. This one is a dialog like the others - centered on
    // its owner, the verb on the button instead of Yes/No.
    sealed class ConfirmDialog : AppDialog
    {
        const int W = 368, Mx = 14, TextW = W - Mx * 2;

        public ConfirmDialog(string title, string message, string verb, bool destructive) : base(title)
        {
            var l = new Label();
            l.Text = message;
            l.Location = new Point(Mx, 12);
            l.AutoSize = false;
            // Measured the way Muted measures: at the scaled width, in the
            // scaled font, expressed back in 100% units for ApplyScale
            int shown = TextRenderer.MeasureText(message, Font, new Size(Theme.S(TextW) - Theme.S(6), 1000),
                        TextFormatFlags.WordBreak).Height + Theme.S(6);
            int h = (int)Math.Ceiling(shown / Theme.Scale);
            l.Size = new Size(TextW, h);
            Controls.Add(l);

            int by = 12 + h + 14;
            ClientSize = new Size(W, by + 28 + 12);
            ChipButton ok = Btn(verb, W - Mx - 82 - 8 - 82, by, 82, 28);
            ChipButton cancel = Btn("Cancel", W - Mx - 82, by, 82, 28);
            ok.Style = delegate { return destructive ? Theme.RemoveChip : Theme.GoChip; };
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static bool Ask(IWin32Window owner, string title, string message, string verb, bool destructive)
        {
            using (var d = new ConfirmDialog(title, message, verb, destructive))
                return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    // --- Settings ----------------------------------------------------------
    sealed class SettingsDialog : AppDialog
    {
        readonly HotkeyBox stopBox = new HotkeyBox();
        readonly HotkeyBox killBox = new HotkeyBox();
        readonly HotkeyBox recBox = new HotkeyBox();
        ChipButton updBtn;
        Label updLbl;
        string newerTag;                // known newer release; the button opens its page
        readonly DropButton scaleDDL = new DropButton();

        public string StopAllKey  { get { return stopBox.Spec; } }
        public string KillKey     { get { return killBox.Spec; } }
        public string RecordKey   { get { return recBox.Spec; } }
        public int UiScale
        {
            get
            {
                int v;
                return int.TryParse(scaleDDL.Text.TrimEnd('%'), out v) ? v : 100;
            }
        }

        readonly ToolTip tips = new ToolTip();

        // The check runs when the window opens - after the handle exists, so
        // the reply can be marshalled back - and again on the button
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            StartUpdateCheck();
        }

        void StartUpdateCheck()
        {
            newerTag = null;
            updBtn.Enabled = false;
            updLbl.Text = "Checking\u2026";
            Updates.Check(this, delegate(string tag, bool newer)
            {
                if (IsDisposed) return;
                updBtn.Enabled = true;
                if (tag == null) updLbl.Text = "Couldn't reach github.com";
                else if (!newer) updLbl.Text = "Up to date (" + Updates.Current() + ")";
                else
                {
                    newerTag = tag;
                    updLbl.Text = tag + " is available";
                    updBtn.Text = "Get " + tag;
                    updBtn.Style = delegate { return Theme.GoChip; };
                    updBtn.Invalidate();
                }
            });
        }

        public SettingsDialog(AppConfig cfg, string recordKeyShown, Action<string> onThemeChange)
            : base("Settings")
        {
            Disposed += delegate { tips.Dispose(); };
            // The same computed flow as the Advanced dialog: a running cursor
            // positions each group, and each group is sized to what it holds -
            // equal margins by construction, nothing to clip.
            const int Mx = 12;              // outer margin, all four sides
            const int Gw = 452;             // group width
            const int Ix = 26;              // content indent
            const int Iw = 424;             // content width inside a group
            const int CapH = 26;            // group caption band
            const int Pad = 12;             // padding inside a group's bottom
            const int Gap = 10;             // between groups
            int y = Mx, cy;

            var gHot = new GroupBox();
            gHot.Text = " Global hotkeys ";
            gHot.Location = new Point(Mx, y);
            Controls.Add(gHot);
            cy = y + CapH;
            cy = Muted("Work in any window. Backspace clears a box.", Ix, cy, Iw).Bottom + 4;

            string[] labels = { "Emergency stop", "Toggle all hotkeys", "Start / stop recording" };
            HotkeyBox[] boxes = { stopBox, killBox, recBox };
            string[] values = { cfg.StopAllKey, cfg.KillSwitchKey, cfg.RecordKey };
            for (int i = 0; i < 3; i++)
            {
                Plain(labels[i], Ix, cy + 4, 214);
                boxes[i].SetBounds(250, cy, 200, 25);
                boxes[i].Spec = values[i];
                Controls.Add(boxes[i]);
                boxes[i].BringToFront();
                cy += i < 2 ? 34 : 25;      // last row: its own height, no pitch
            }
            gHot.Size = new Size(Gw, cy + Pad - y);
            y = gHot.Bottom + Gap;

            var gMac = new GroupBox();
            gMac.Text = " Macros ";
            gMac.Location = new Point(Mx, y);
            Controls.Add(gMac);
            cy = y + CapH;
            cy = Muted("Ready a clicker with its ⏺ button, then press " + recordKeyShown
                + " to start and stop recording.", Ix, cy, Iw).Bottom + 6;
            ChipButton openBtn = IconBtn("folder", "Open Macros folder", Ix, cy, 180, 28);
            openBtn.Click += delegate
            {
                try
                {
                    System.IO.Directory.CreateDirectory(AppConfig.MacroDir);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + AppConfig.MacroDir + "\"");
                }
                catch { }
            };
            cy += 28;
            gMac.Size = new Size(Gw, cy + Pad - y);
            y = gMac.Bottom + Gap;

            var gTheme = new GroupBox();
            gTheme.Text = " Appearance ";
            gTheme.Location = new Point(Mx, y);
            Controls.Add(gTheme);
            cy = y + CapH;
            // Applies the instant it's clicked - the main window changes
            // behind this dialog, and the dialog itself follows suit
            string[] modes = { "light", "dark", "system" };
            string[] modeLabels = { "Light", "Dark", "System default" };
            int rx = Ix;
            for (int i = 0; i < 3; i++)
            {
                var rb = new RadioDot();
                rb.Text = modeLabels[i];
                // measured with the scaled font, laid out in 100% units
                int tw = (int)Math.Ceiling(TextRenderer.MeasureText(modeLabels[i], Font).Width / Theme.Scale);
                rb.SetBounds(rx, cy, tw + 26, 20);
                rb.Checked = cfg.ThemeMode == modes[i];
                string mode = modes[i];
                rb.CheckedChanged += delegate(object s, EventArgs ev)
                {
                    if (!((RadioDot)s).Checked) return;
                    if (onThemeChange != null) onThemeChange(mode);
                    ReTheme();
                };
                Controls.Add(rb);
                rb.BringToFront();
                rx += tw + 46;
            }
            cy += 20 + 8;

            // On top of the display's DPI: a 125% laptop screen at 80% draws
            // the layout the size it has at 100%
            Plain("UI size", Ix, cy + 4, 60);
            foreach (int pct in new[] { 75, 90, 100, 110, 125, 150, 175, 200 })
                scaleDDL.Items.Add(pct + "%");
            scaleDDL.Text = cfg.UiScale + "%";
            scaleDDL.SetBounds(Ix + 64, cy, 80, 25);
            Controls.Add(scaleDDL);
            scaleDDL.BringToFront();
            Label hint = Plain("Ctrl+=  Ctrl+-  Ctrl+0 anywhere", Ix + 154, cy + 4, 220);
            hint.ForeColor = Color.Gray;
            cy += 25;
            gTheme.Size = new Size(Gw, cy + Pad - y);
            y = gTheme.Bottom + Gap;

            var gUpd = new GroupBox();
            gUpd.Text = " Updates ";
            gUpd.Location = new Point(Mx, y);
            Controls.Add(gUpd);
            cy = y + CapH;
            updBtn = Btn("Check for updates", Ix, cy, 150, 28);
            updBtn.BringToFront();
            updBtn.Click += delegate
            {
                if (newerTag == null) { StartUpdateCheck(); return; }
                try { System.Diagnostics.Process.Start(Updates.ReleasesUrl); } catch { }
            };
            tips.SetToolTip(updBtn, "Ask github.com for the latest release");
            updLbl = Plain("", Ix + 158, cy + 5, Iw - 158);
            updLbl.ForeColor = Color.Gray;
            updLbl.BringToFront();
            cy += 28;
            gUpd.Size = new Size(Gw, cy + Pad - y);
            y = gUpd.Bottom + Gap + 4;

            ChipButton resetBtn = IconBtn("undo", "Defaults", Mx, y, 100, 28);
            resetBtn.Click += delegate
            {
                // The defaults live in one place - a fresh config's field
                // initialisers - so this can't drift from what a new install gets
                var fresh = new AppConfig();
                stopBox.Spec = fresh.StopAllKey;
                killBox.Spec = fresh.KillSwitchKey;
                recBox.Spec = fresh.RecordKey;
            };
            tips.SetToolTip(resetBtn, "Restore the default hotkeys");

            // The current cards and their takes as one zip, for another machine
            ChipButton exportBtn = IconBtn("save", "Export profile…", Mx + 106, y, 148, 28);
            exportBtn.Click += delegate
            {
                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Export profile";
                    sfd.Filter = "Polyclicker profile bundle (*.zip)|*.zip";
                    sfd.FileName = (cfg.CurrentProfile.Length > 0
                        ? cfg.CurrentProfile : "Polyclicker profile") + ".zip";
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    try { cfg.ExportBundle(sfd.FileName); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Couldn't export it:\n\n" + ex.Message,
                            "Export profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };
            tips.SetToolTip(exportBtn, "Save the current cards and the takes they use"
                + " as a zip; unpack it into %APPDATA%\\Polyclicker elsewhere");

            // right edge aligned with the group boxes above
            ChipButton ok = Btn("OK", Mx + Gw - 174, y, 82, 28);
            ChipButton cancel = Btn("Cancel", Mx + Gw - 82, y, 82, 28);
            ok.Style = delegate { return Theme.GoChip; };
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(Gw + Mx * 2, y + 28 + Mx);

            foreach (var g in new[] { gHot, gMac, gTheme, gUpd }) g.SendToBack();
        }
    }

    // --- per-card advanced options (the gear) ------------------------------
    sealed class AdvancedDialog : AppDialog
    {
        readonly SlotConfig o;
        readonly DropButton modeDDL = new DropButton();
        readonly DropButton winDDL = new DropButton();
        readonly List<string> winValues = new List<string>();
        string winValue;                        // the gate as stored in the INI
        const string Anywhere = "(Anywhere)";
        readonly CheckBox focusChk = new CheckBox();
        readonly NumberBox clicksEdit = new NumberBox();
        readonly NumberBox secsEdit = new NumberBox();
        readonly NumberBox jitterEdit = new NumberBox();
        readonly NumberBox stepJitEdit = new NumberBox();
        readonly NumberBox posJitEdit = new NumberBox();
        readonly CheckBox restoreChk = new CheckBox();
        readonly CheckBox relChk = new CheckBox();
        readonly CheckBox flickChk = new CheckBox();
        readonly NumberBox holdEdit = new NumberBox();
        readonly CheckBox lockChk = new CheckBox();
        readonly CheckBox stopMouseChk = new CheckBox();
        readonly CheckBox stopKeysChk = new CheckBox();
        readonly NumberBox gapEdit = new NumberBox();
        readonly NumberBox delayEdit = new NumberBox();
        readonly Field atEdit = new Field();
        Label holdHint;

        public bool ModeChanged;

        public AdvancedDialog(SlotConfig cfg, string cardLabel) : base(cardLabel + " - Advanced")
        {
            o = cfg;
            // The dialog shows only what the card's input type actually uses:
            // a macro has no hold or position of its own, a key has no
            // pointer, and only mouse clicks can be posted into a background
            // window. Hidden settings keep their stored values untouched.
            bool isMacro = cfg.IsMacro;
            bool isMouse = !isMacro && !cfg.IsCustomKey;
            string unit = isMacro ? "repeats" : "clicks";

            // One grid for the whole dialog. Every group is positioned from a
            // running cursor and sized to whatever it ends up holding, so the
            // margins stay equal and nothing can be clipped by a stale
            // hardcoded height when a string or a control changes.
            const int Mx = 12;              // outer margin, all four sides
            const int Gw = 394;             // group width
            const int Iw = 366;             // content width inside a group
            const int CapH = 26;            // group caption band
            const int Pad = 12;             // padding inside a group's bottom
            const int Gap = 10;             // between groups
            // Two columns, each with its own running cursor: what starts and
            // where it runs on the left; what stops it and how it clicks on
            // the right. One column had outgrown comfortable dialog height.
            int xL = Mx, xR = Mx + Gw + Gap;
            int ixL = xL + 14, ixR = xR + 14;
            int yL = Mx, yR = Mx, cy;

            // --- Trigger (left) --------------------------------------------
            var gTrig = new GroupBox();
            gTrig.Text = " Trigger ";
            gTrig.Location = new Point(xL, yL);
            Controls.Add(gTrig);
            cy = yL + CapH;
            Plain("Mode:", ixL, cy + 4, 66);
            modeDDL.Items.Add("Toggle  -  press once to start, again to stop");
            modeDDL.Items.Add("Hold  -  runs only while the key is held");
            modeDDL.SetBounds(xL + 88, cy, 290, 26);
            modeDDL.Text = modeDDL.Items[cfg.Mode == "Hold" ? 1 : 0];
            Controls.Add(modeDDL);
            modeDDL.BringToFront();
            cy += 26 + 6;
            // Later starts: a countdown, a wall-clock time, or both
            Plain("Start delay", ixL, cy + 4, 80);
            delayEdit.SetBounds(xL + 98, cy, 50, 25);
            delayEdit.Text = cfg.StartDelaySec.ToString();
            Controls.Add(delayEdit); delayEdit.BringToFront();
            Plain("s", xL + 154, cy + 4, 18);
            Plain("Start at", xL + 198, cy + 4, 56);
            atEdit.SetBounds(xL + 258, cy, 56, 25);
            atEdit.Text = cfg.StartAt;
            Controls.Add(atEdit); atEdit.BringToFront();
            Plain("(HH:MM)", xL + 320, cy + 4, 62);
            cy += 25 + 6;
            cy = Muted("Starts after the delay, at the next HH:MM, or both."
                + " The hotkey again cancels the wait.", ixL, cy, Iw).Bottom + 4;
            lockChk.Text = "Keep running while locked";
            lockChk.SetBounds(ixL, cy, Iw, 22);
            lockChk.Checked = cfg.KeepWhileLocked;
            Controls.Add(lockChk);
            lockChk.BringToFront();
            cy += 26;
            cy = Muted("The lock only blocks the hotkey; the card keeps running.",
                ixL, cy, Iw).Bottom;
            gTrig.Size = new Size(Gw, cy + Pad - yL);
            yL = gTrig.Bottom + Gap;

            // --- Where it runs (left) --------------------------------------
            var gWin = new GroupBox();
            gWin.Text = " Where it runs ";
            gWin.Location = new Point(xL, yL);
            Controls.Add(gWin);
            cy = yL + CapH;
            Plain("Window:", ixL, cy + 4, 66);
            winValue = cfg.WinTitle.Trim();
            winDDL.SetBounds(xL + 88, cy, 206, 26);
            winDDL.Text = winValue.Length == 0 ? Anywhere : cfg.GateName();
            winDDL.Opening += delegate { PopulateWindows(); };
            winDDL.Picked += delegate(string display)
            {
                int i = winDDL.Items.IndexOf(display);
                if (i >= 0 && i < winValues.Count) winValue = winValues[i];
            };
            Controls.Add(winDDL);
            winDDL.BringToFront();

            // Point, don't browse: click the button, then click the window
            ChipButton pickBtn = IconBtn("target", "Pick", xL + 300, cy - 1, 78, 27);
            pickBtn.BringToFront();
            // A ToolTip is a component, not a child control - the form's
            // Dispose never reaches it unless it's wired up by hand
            var pickTip = new ToolTip();
            pickTip.SetToolTip(pickBtn, "Then click the window you want (right-click cancels)");
            Disposed += delegate { pickTip.Dispose(); };
            pickBtn.Click += delegate
            {
                // Step aside WITHOUT hiding: turning Visible off on a modal
                // dialog ends its ShowDialog loop. Off-screen keeps the modal
                // session alive while the user clicks the window they want.
                Point home = Location;
                Location = new Point(-4000, home.Y);
                CursorToast.Pop("Click the window to target  ·  right-click cancels");
                WindowPick.Once(this, delegate(IntPtr h)
                {
                    Location = home;
                    Activate();
                    if (h == IntPtr.Zero) return;
                    string exe = WindowMatcher.ExeOf(h);
                    if (exe.Length == 0) return;
                    winValue = "ahk_exe " + exe;
                    winDDL.Text = exe;
                });
            };
            cy += 26 + 6;
            focusChk.Text = "Bring that window up when the hotkey is pressed";
            // Full group width: at the edit's indent the tail of the sentence
            // was cut off
            focusChk.SetBounds(ixL, cy, Iw, 22);
            focusChk.Checked = cfg.FocusWindow;
            Controls.Add(focusChk);
            focusChk.BringToFront();
            cy += 26;

            // Keep-running: the card clicks the gate window in the background
            // instead of pausing when the user switches away. Posting clicks
            // is the mouse path only - macros and keys can't run from behind.
            if (isMouse)
            {
                flickChk.Text = "Keep running when I switch away";
                flickChk.SetBounds(ixL, cy, Iw, 22);
                flickChk.Checked = cfg.FlickFocus;
                Controls.Add(flickChk);
                flickChk.BringToFront();
                cy += 26;
            }
            cy = Muted(isMouse
                ? "With a window set, it starts in front and pauses when you switch"
                + " away, unless kept running in the background."
                : "With a window set, it starts in front and pauses when you switch away.",
                ixL, cy, Iw).Bottom;
            gWin.Size = new Size(Gw, cy + Pad - yL);
            yL = gWin.Bottom + Gap;

            // --- Stop automatically (right) --------------------------------
            var gStop = new GroupBox();
            gStop.Text = " Stop automatically ";
            gStop.Location = new Point(xR, yR);
            Controls.Add(gStop);
            cy = yR + CapH;
            Plain("After:", ixR, cy + 4, 66);
            clicksEdit.SetBounds(xR + 88, cy, 56, 25);
            clicksEdit.Text = cfg.StopClicks.ToString();
            Controls.Add(clicksEdit); clicksEdit.BringToFront();
            Plain(unit, xR + 150, cy + 4, 62);
            Plain("or", xR + 226, cy + 4, 24);
            secsEdit.SetBounds(xR + 252, cy, 56, 25);
            secsEdit.Text = cfg.StopSeconds.ToString();
            Controls.Add(secsEdit); secsEdit.BringToFront();
            Plain("seconds", xR + 314, cy + 4, 64);
            cy += 25 + 6;
            cy = Muted("0 = no limit. Whichever hits first.", xR + 88, cy, 292).Bottom + 4;
            stopMouseChk.Text = "Stop when I use the mouse";
            stopMouseChk.SetBounds(ixR, cy, Iw, 22);
            stopMouseChk.Checked = cfg.StopOnMouse;
            Controls.Add(stopMouseChk);
            stopMouseChk.BringToFront();
            cy += 26;
            stopKeysChk.Text = "Stop when I use the keyboard";
            stopKeysChk.SetBounds(ixR, cy, Iw, 22);
            stopKeysChk.Checked = cfg.StopOnKeys;
            Controls.Add(stopKeysChk);
            stopKeysChk.BringToFront();
            cy += 26;
            cy = Muted(isMacro
                ? "A click, scroll, or key stops it. Pointer moves don't count while"
                + " the take drives the pointer."
                : "A click, scroll, key, or real movement stops it.", ixR, cy, Iw).Bottom;
            gStop.Size = new Size(Gw, cy + Pad - yR);
            yR = gStop.Bottom + Gap;

            // --- Each click (right; clicks and keys) / Looping (macros) -----
            // A macro replays its own press lengths, so it gets the looping
            // settings here instead: the gap between repeats, gap 0 = replay
            // back-to-back. The loop toggle and speed live on the card.
            var gClick = new GroupBox();
            gClick.Text = isMacro ? " Looping " : " Each click ";
            gClick.Location = new Point(xR, yR);
            Controls.Add(gClick);
            cy = yR + CapH;
            if (isMacro)
            {
                Plain("Loop gap", ixR, cy + 4, 64);
                gapEdit.SetBounds(xR + 82, cy, 56, 25);
                gapEdit.Text = cfg.Interval.ToString();
                Controls.Add(gapEdit); gapEdit.BringToFront();
                Plain("ms", xR + 144, cy + 4, 26);
                cy += 25 + 6;
                cy = Muted("Pause between loops. 0 replays back-to-back.", ixR, cy, Iw).Bottom;
            }
            else
            {
                Plain("Hold for", ixR, cy + 4, 64);
                holdEdit.SetBounds(xR + 82, cy, 46, 25);
                holdEdit.Text = cfg.HoldPercent.ToString();
                Controls.Add(holdEdit); holdEdit.BringToFront();
                Plain("% of the interval", xR + 134, cy + 4, 116);
                // Built by hand, not Muted(): that sizes to its text, and an empty
                // string would give a 4 px tall label the live hint can't show in
                holdHint = new Label();
                holdHint.ForeColor = Color.Gray;
                holdHint.SetBounds(xR + 256, cy + 4, 124, 20);
                Controls.Add(holdHint);
                holdHint.BringToFront();
                // Percent is the setting, but milliseconds is what a person
                // pictures - show both, live
                EventHandler refreshHint = delegate
                {
                    int pct;
                    if (!int.TryParse(holdEdit.Text, out pct)) pct = 0;
                    pct = Math.Max(0, Math.Min(99, pct));
                    holdHint.Text = pct == 0
                        ? "instant (default)"
                        : "= " + (Math.Max(1, cfg.Interval) * pct / 100) + " ms per click";
                };
                holdEdit.TextChanged += refreshHint;
                refreshHint(null, EventArgs.Empty);
                cy += 25 + 6;
                cy = Muted("Share of the interval the button stays down. To hold it"
                    + " permanently, use the toggle by the interval.", ixR, cy, Iw).Bottom;
            }
            gClick.Size = new Size(Gw, cy + Pad - yR);
            yR = gClick.Bottom + Gap;

            // --- Randomise (right) -----------------------------------------
            var gRand = new GroupBox();
            gRand.Text = " Randomise ";
            gRand.Location = new Point(xR, yR);
            Controls.Add(gRand);
            cy = yR + CapH;
            Plain(isMacro ? "Loop gap ±" : "Interval ±", ixR, cy + 4, 74);
            jitterEdit.SetBounds(xR + 92, cy, 50, 25);
            jitterEdit.Text = cfg.JitterMs.ToString();
            Controls.Add(jitterEdit); jitterEdit.BringToFront();
            Plain("ms", xR + 148, cy + 4, 26);
            if (isMouse)
            {
                Plain("Position ±", xR + 206, cy + 4, 76);
                posJitEdit.SetBounds(xR + 284, cy, 50, 25);
                posJitEdit.Text = cfg.PosJitter.ToString();
                Controls.Add(posJitEdit); posJitEdit.BringToFront();
                Plain("px", xR + 340, cy + 4, 26);
            }
            else if (isMacro)
            {
                Plain("Steps ±", xR + 206, cy + 4, 60);
                stepJitEdit.SetBounds(xR + 268, cy, 50, 25);
                stepJitEdit.Text = cfg.MacroJitterMs.ToString();
                Controls.Add(stepJitEdit); stepJitEdit.BringToFront();
                Plain("ms", xR + 324, cy + 4, 26);
            }
            cy += 25 + 6;
            cy = Muted(isMouse
                ? "0 = exact. Position needs Fixed Position."
                : isMacro
                ? "0 = exact. Steps nudges every press and release by up to that much."
                : "0 = exact timing.", ixR, cy, Iw).Bottom;
            gRand.Size = new Size(Gw, cy + Pad - yR);
            yR = gRand.Bottom + Gap;

            // --- Misc (left, under the groups) -----------------------------
            if (isMouse)
            {
                restoreChk.Text = "Put the cursor back afterwards (fixed-position clicking only)";
                restoreChk.SetBounds(xL + 2, yL, Gw, 22);
                restoreChk.Checked = cfg.RestoreCursor;
                Controls.Add(restoreChk);
                yL += 26;
            }
            if (isMacro)
            {
                // Replay anchored to wherever the window is now rather than
                // to the screen. The recording carries the window's position
                // and size; a size mismatch warns on the card at playback.
                relChk.Text = "Follow the window: replay positions relative to where it is now";
                relChk.SetBounds(xL + 2, yL, Gw, 22);
                relChk.Checked = cfg.MacroRelative;
                Controls.Add(relChk);
                yL += 26;
            }
            yL += Gap;

            int y = Math.Max(yL, yR);
            int fullW = Gw * 2 + Gap;
            ChipButton ok = Btn("OK", Mx + fullW - 174, y, 82, 28);
            ChipButton cancel = Btn("Cancel", Mx + fullW - 82, y, 82, 28);
            // The margin that opened the dialog closes it
            ClientSize = new Size(fullW + Mx * 2, y + 28 + Mx);
            ok.Style = delegate { return Theme.GoChip; };
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += delegate { Apply(); };

            foreach (var g in new[] { gTrig, gWin, gStop, gClick, gRand }) g.SendToBack();
        }

        // The dropdown lists what is open right now, one entry per program -
        // built when it opens, so it is never stale
        void PopulateWindows()
        {
            winDDL.Items.Clear();
            winValues.Clear();
            winDDL.Items.Add(Anywhere);
            winValues.Add("");
            var seen = new List<string>();
            foreach (KeyValuePair<string, string> w in WindowMatcher.OpenWindows(IntPtr.Zero))
            {
                string exe = w.Value;
                if (exe.Length == 0) continue;
                bool dup = false;
                foreach (string sx in seen)
                    if (string.Equals(sx, exe, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (dup) continue;
                seen.Add(exe);
                winDDL.Items.Add(exe);
                winValues.Add("ahk_exe " + exe);
            }
            // a saved gate that isn't running right now (or a title match)
            // stays selectable rather than silently vanishing
            if (winValue.Length > 0 && !winValues.Contains(winValue))
            {
                winDDL.Items.Insert(1, o.GateName());
                winValues.Insert(1, winValue);
            }
        }

        void Apply()
        {
            string mode = modeDDL.Text.StartsWith("Hold") ? "Hold" : "Toggle";
            ModeChanged = o.Mode != mode;
            o.Mode = mode;
            o.WinTitle = winValue;
            // Only controls the dialog actually showed write back - a hidden
            // setting keeps whatever the card had stored
            o.StopClicks  = Num(clicksEdit, int.MaxValue);
            o.StopSeconds = Num(secsEdit, int.MaxValue);
            o.JitterMs    = Num(jitterEdit, int.MaxValue);
            if (posJitEdit.Parent != null) o.PosJitter = Num(posJitEdit, int.MaxValue);
            if (stepJitEdit.Parent != null) o.MacroJitterMs = Num(stepJitEdit, 5000);
            if (holdEdit.Parent != null) o.HoldPercent = Num(holdEdit, 99);
            if (gapEdit.Parent != null) o.Interval = Num(gapEdit, int.MaxValue);
            o.StartDelaySec = Num(delayEdit, 86400);
            // Anything that doesn't read as a time of day clears the schedule
            // rather than silently keeping a value the box no longer shows
            int hh, mm;
            o.StartAt = SlotConfig.TryParseStartAt(atEdit.Text, out hh, out mm)
                      ? hh.ToString("00") + ":" + mm.ToString("00") : "";
            o.KeepWhileLocked = lockChk.Checked;
            o.StopOnMouse = stopMouseChk.Checked;
            o.StopOnKeys = stopKeysChk.Checked;
            if (restoreChk.Parent != null) o.RestoreCursor = restoreChk.Checked;
            o.FocusWindow = focusChk.Checked;
            if (flickChk.Parent != null) o.FlickFocus = flickChk.Checked;
            if (relChk.Parent != null) o.MacroRelative = relChk.Checked;
        }

        // A number box's value, clamped to 0..max; unparseable reads as 0
        static int Num(TextBox box, int max)
        {
            int v;
            return int.TryParse(box.Text, out v) ? Math.Max(0, Math.Min(max, v)) : 0;
        }
    }

}
