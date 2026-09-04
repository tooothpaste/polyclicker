// Reproduces the two crashes from the field: the disposed-CursorToast on
// back-to-back Pops, and the ListBox IndexOutOfRange when ClearSelected
// fires SelectedIndexChanged mid-mutation with a multi-row selection.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polyclicker
{
    static class ReproMain
    {
        delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr mod, uint tid);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hk, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hk);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandleW(string name);
        static void Pump(int ms)
        {
            int end = Environment.TickCount + ms;
            while (Environment.TickCount < end)
            { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            string take = args[0];

            // --- toast patterns ------------------------------------------
            try
            {
                CursorToast.Pop("one");
                CursorToast.Pop("two");            // immediate replace
                Pump(50);
                CursorToast.Pop("three");
                Pump(1400);                        // let life expire, tick runs
                CursorToast.Pop("four");
                Pump(50);
                CursorToast.Pop("five", 1100, true);   // follow variant
                CursorToast.Pop("six");
                Pump(50);
                Console.WriteLine("toast rapid-fire: no throw");
            }
            catch (Exception ex)
            {
                Console.WriteLine("TOAST THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            // --- dialog: multi-select then W (BeginCellEdit->ClearSelected) --
            var d = new MacroEditorDialog(take, "Repro", null);
            d.Show();
            Pump(200);
            var listF = typeof(MacroEditorDialog).GetField("list",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var lb = (ListBox)listF.GetValue(d);
            var begin = typeof(MacroEditorDialog).GetMethod("BeginCellEdit",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var cellT = typeof(MacroEditorDialog).GetNestedType("Cell",
                BindingFlags.NonPublic);
            object cellWait = Enum.ToObject(cellT, 0);
            try
            {
                for (int i = 0; i < lb.Items.Count; i++) lb.SetSelected(i, true);
                Pump(50);
                begin.Invoke(d, new object[] { 2, cellWait }); // like pressing Enter
                Pump(100);
                Console.WriteLine("multi-select + cell edit: no throw");
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("LIST THREW: " + inner.GetType().Name + ": " + inner.Message);
            }

            // --- multi-select then a refresh (Items.Clear clears selection
            //     and fires SelectedIndexChanged mid-teardown) ---------------
            try
            {
                for (int i = 0; i < lb.Items.Count; i++) lb.SetSelected(i, true);
                Pump(50);
                var refresh = typeof(MacroEditorDialog).GetMethod("RefreshRows",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                refresh.Invoke(d, new object[] { 3 });
                Pump(100);
                Console.WriteLine("multi-select + refresh: no throw");
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("REFRESH THREW: " + inner.GetType().Name + ": " + inner.Message);
            }

            // --- same, but via the real commit path: cell edit open, then
            //     commit while many rows are selected ------------------------
            try
            {
                begin.Invoke(d, new object[] { 1, cellWait });
                Pump(50);
                for (int i = 0; i < lb.Items.Count; i++) lb.SetSelected(i, true);
                Pump(50);
                var end = typeof(MacroEditorDialog).GetMethod("EndCellEdit",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                end.Invoke(d, new object[] { true });
                Pump(100);
                Console.WriteLine("multi-select + commit: no throw");
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("COMMIT THREW: " + inner.GetType().Name + ": " + inner.Message);
            }

            // --- keyboard-built extended selection, then the W key ----------
            try
            {
                d.Activate();
                lb.Focus();
                Pump(100);
                SendKeys.SendWait("{HOME}");
                SendKeys.SendWait("+{DOWN}+{DOWN}+{DOWN}");
                Pump(100);
                SendKeys.SendWait("w");
                Pump(150);
                SendKeys.SendWait("{TAB}");        // tab out of the cell editor
                Pump(150);
                SendKeys.SendWait("w");
                Pump(150);
                Console.WriteLine("keyboard selection + W/Tab/W: no throw");
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("KEYS THREW: " + inner.GetType().Name + ": " + inner.Message);
            }

            // --- the Excel walk: W opens wait, Tab hops to length, Tab again
            //     lands on the next row's wait, Shift+Tab walks back ---------
            try
            {
                var rowF = typeof(MacroEditorDialog).GetField("cellRow",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var lenF = typeof(MacroEditorDialog).GetField("cellKind",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                d.Activate(); lb.Focus(); Pump(100);
                SendKeys.SendWait("{HOME}"); Pump(50);
                SendKeys.SendWait("{DOWN}"); Pump(50);       // row 1: the click
                SendKeys.SendWait("{F2}"); Pump(150);
                Console.WriteLine("after W: row=" + rowF.GetValue(d)
                    + " len=" + lenF.GetValue(d));
                SendKeys.SendWait("{TAB}"); Pump(150);
                Console.WriteLine("after Tab: row=" + rowF.GetValue(d)
                    + " len=" + lenF.GetValue(d));
                SendKeys.SendWait("{TAB}"); Pump(150);
                Console.WriteLine("after Tab Tab: row=" + rowF.GetValue(d)
                    + " len=" + lenF.GetValue(d));
                SendKeys.SendWait("+{TAB}"); Pump(150);
                Console.WriteLine("after Shift+Tab: row=" + rowF.GetValue(d)
                    + " len=" + lenF.GetValue(d));
                var cellF = typeof(MacroEditorDialog).GetField("cellEdit",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var ce = (Control)cellF.GetValue(d);
                Console.WriteLine("pre-Esc: visible=" + ce.Visible
                    + " focused=" + ce.Focused);
                SendKeys.SendWait("{ESC}"); Pump(150);
                Console.WriteLine("after Esc: row=" + rowF.GetValue(d)
                    + " visible=" + ce.Visible + " editor open=" + !d.IsDisposed);

                // Alt+Down moves the selected row; Ctrl+D duplicates it;
                // Ctrl+C then Ctrl+V pastes a copy from the real clipboard
                var takeF = typeof(MacroEditorDialog).GetField("take",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var tb = (TakeBuffer)takeF.GetValue(d);
                lb.Focus(); Pump(50);
                SendKeys.SendWait("{HOME}{DOWN}"); Pump(50);
                string before = tb.Rows[1].Desc;
                SendKeys.SendWait("%{DOWN}"); Pump(150);
                Console.WriteLine("Alt+Down: row1 was '" + before + "', now '"
                    + tb.Rows[1].Desc + "', row2 '" + tb.Rows[2].Desc + "'");
                SendKeys.SendWait("%{UP}"); Pump(150);
                Console.WriteLine("Alt+Up back: row1 '" + tb.Rows[1].Desc + "'");
                SendKeys.SendWait("^{DOWN}"); Pump(150);
                Console.WriteLine("Ctrl+Down: row1 '" + tb.Rows[1].Desc
                    + "', row2 '" + tb.Rows[2].Desc + "'");
                SendKeys.SendWait("^{UP}"); Pump(150);
                Console.WriteLine("Ctrl+Up back: row1 '" + tb.Rows[1].Desc + "'");
                // the toolbar chevron must move the step, then hand the
                // keyboard back to the list
                var upF = typeof(MacroEditorDialog).GetField("downBtn",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var chev = (ChipButton)upF.GetValue(d);
                chev.Focus(); Pump(50);            // as a real click would
                chev.PerformClick(); Pump(150);
                Console.WriteLine("chevron click: row1 '" + tb.Rows[1].Desc
                    + "', row2 '" + tb.Rows[2].Desc + "', listFocused=" + lb.Focused);
                SendKeys.SendWait("^{UP}"); Pump(150);
                Console.WriteLine("Ctrl+Up right after the click: row1 '"
                    + tb.Rows[1].Desc + "'");
                int rows0 = tb.Rows.Count;
                SendKeys.SendWait("^d"); Pump(150);
                Console.WriteLine("Ctrl+D: rows " + rows0 + " -> " + tb.Rows.Count);
                for (int tries = 0; tries < 5; tries++)
                {
                    try { Clipboard.SetText("sentinel"); break; }
                    catch { Pump(50); }
                }
                SendKeys.SendWait("^c"); Pump(200);
                Console.WriteLine("clipboard after ^c: '"
                    + Clipboard.GetText().Split('\n')[0].Trim() + "'");
                int rowsBeforeV = tb.Rows.Count;
                lb.KeyDown += delegate(object s2, KeyEventArgs e2)
                { Console.WriteLine("  list saw key: " + e2.KeyData); };
                Console.WriteLine("  before ^v: listFocused=" + lb.Focused
                    + " activeForm=" + (Form.ActiveForm == null ? "null" : Form.ActiveForm.Text)
                    + " activeCtl=" + (d.ActiveControl == null ? "null" : d.ActiveControl.GetType().Name));
                SendKeys.SendWait("^v"); Pump(200);
                Console.WriteLine("^v added " + (tb.Rows.Count - rowsBeforeV) + " row(s)");
                Console.WriteLine("Ctrl+C/V: rows -> " + tb.Rows.Count
                    + ", clipboard starts '" + (Clipboard.GetText().Split('\n')[0]).Trim() + "'");
                var paste = typeof(MacroEditorDialog).GetMethod("PasteSteps",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                paste.Invoke(d, null); Pump(100);
                Console.WriteLine("direct PasteSteps: rows -> " + tb.Rows.Count);
                REv[] ps = TakeBuffer.ParseSteps(Clipboard.GetText());
                Console.WriteLine("ParseSteps of clipboard: "
                    + (ps == null ? "null" : ps.Length + " events"));
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("WALK THREW: " + inner.GetType().Name + ": " + inner.Message);
            }

            // --- a held key ticks out typematic repeats like a real finger --
            try
            {
                int downs = 0;
                HookProc proc = delegate(int code, IntPtr wp, IntPtr lp)
                {
                    if (code >= 0 && ((int)wp == 0x0100 || (int)wp == 0x0104)
                        && Marshal.ReadInt32(lp) == 0x7C)
                        downs++;
                    return CallNextHookEx(IntPtr.Zero, code, wp, lp);
                };
                IntPtr hk = SetWindowsHookExW(13, proc, GetModuleHandleW(null), 0);
                var hcfg = new SlotConfig();
                hcfg.Input = "Custom Key";
                hcfg.CustomKey = "F13";
                hcfg.Interval = 50;
                hcfg.HoldDown = true;
                Engine.Start(96, hcfg, IntPtr.Zero, 0);
                Pump(600);
                Engine.Stop(96);
                Pump(200);
                UnhookWindowsHookEx(hk);
                GC.KeepAlive(proc);
                Console.WriteLine("typematic: " + downs + " F13 downs in 600 ms "
                    + (downs >= 10 ? "(repeating like a held key)" : "(NOT repeating)"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("TYPEMATIC THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            // --- name collision toast then save toast (field sequence) ------
            try
            {
                CursorToast.Pop("A macro called 'x' already exists");
                var save = typeof(MacroEditorDialog).GetMethod("SaveFile",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                save.Invoke(d, null);
                Pump(100);
                Console.WriteLine("collision-then-save: no throw");
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("SAVE THREW: " + inner.GetType().Name + ": " + inner.Message);
            }
        }
    }
}
