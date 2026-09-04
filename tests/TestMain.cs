// Harness: start-delay countdown, cancel-during-wait, StartAt parsing,
// and the macro editor's TakeBuffer (grouping, pair-safe delete, timing).
// Compiled against all app sources with /main: - see regression pattern.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Polyclicker
{
    static class TestMain
    {
        static int fails;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) fails++;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);

        // HoldPercent 100: the press goes down once and stays down for the
        // whole run, released the moment the card stops. F13 - a key nothing
        // on a desktop reacts to - stands in for the mouse button.
        static void HoldForeverTests()
        {
            var cfg = new SlotConfig();
            cfg.Input = "Custom Key";
            cfg.CustomKey = "F13";
            cfg.Interval = 50;
            cfg.HoldDown = true;
            Engine.Start(90, cfg, IntPtr.Zero, 0);
            Thread.Sleep(300);
            bool downMid = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
            Thread.Sleep(300);
            bool downLate = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
            Engine.Stop(90);
            Thread.Sleep(250);
            bool downAfter = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
            Check(downMid && downLate, "hold 100%: key stays down through the run");
            Check(!downAfter, "hold 100%: released the moment the card stops");
        }

        // A macro that plays the held key takes it over; once playback goes
        // quiet the hold must press again on its own.
        static void HoldResumeTests()
        {
            var cfg = new SlotConfig();
            cfg.Input = "Custom Key";
            cfg.CustomKey = "F13";
            cfg.Interval = 50;
            cfg.HoldDown = true;
            Engine.Start(92, cfg, IntPtr.Zero, 0);
            Thread.Sleep(300);
            bool heldBefore = (GetAsyncKeyState(0x7C) & 0x8000) != 0;

            string p = Path.Combine(Path.GetTempPath(), "polytest-resume.macro");
            File.WriteAllLines(p, new string[]
            {
                "# Polyclicker macro v1",
                "0.000 3 124 0",
                "60.000 4 124 0",       // the take releases the held key
            });
            var mcfg = new SlotConfig();
            mcfg.Input = "Macro";
            mcfg.MacroLoop = false;
            mcfg.Interval = 0;
            string warn;
            Engine.StartMacro(93, mcfg, IntPtr.Zero, p, out warn);
            Thread.Sleep(250);          // take done; still inside the quiet window
            bool releasedByTake = (GetAsyncKeyState(0x7C) & 0x8000) == 0;
            Thread.Sleep(700);          // quiet window over - the hold resumes
            bool heldAgain = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
            Engine.Stop(92);
            Thread.Sleep(250);
            bool downAfter = (GetAsyncKeyState(0x7C) & 0x8000) != 0;

            Check(heldBefore, "resume: held before the macro");
            Check(releasedByTake, "resume: the take released the key");
            Check(heldAgain, "resume: hold pressed again after playback");
            Check(!downAfter, "resume: released when the card stops");

            // The same story with a mouse button - X2, the one button a
            // desktop ignores. Ev button code 4 = X2, async vk 0x06.
            var bcfg = new SlotConfig();
            bcfg.Input = "X2 Button";
            bcfg.Interval = 50;
            bcfg.HoldDown = true;
            Engine.Start(94, bcfg, IntPtr.Zero, 0);
            Thread.Sleep(300);
            bool bBefore = (GetAsyncKeyState(0x06) & 0x8000) != 0;
            File.WriteAllLines(p, new string[]
            {
                "# Polyclicker macro v1",
                "0.000 1 4 0",
                "60.000 2 4 0",
            });
            Engine.StartMacro(95, mcfg, IntPtr.Zero, p, out warn);
            Thread.Sleep(250);
            bool bReleased = (GetAsyncKeyState(0x06) & 0x8000) == 0;
            Thread.Sleep(700);
            bool bAgain = (GetAsyncKeyState(0x06) & 0x8000) != 0;
            Engine.Stop(94);
            Thread.Sleep(250);
            bool bAfter = (GetAsyncKeyState(0x06) & 0x8000) != 0;
            Check(bBefore, "resume: X2 held before the macro");
            Check(bReleased, "resume: the take released X2");
            Check(bAgain, "resume: X2 pressed again after playback");
            Check(!bAfter, "resume: X2 released when the card stops");
        }

        // A hold-duration cycle is press first, idle after: starting the card
        // presses at once, holds its share of the interval, and the release
        // phase sits at the END of the click. Sampled off the real key state.
        static void HoldPhaseTests()
        {
            var cfg = new SlotConfig();
            cfg.Input = "Custom Key";
            cfg.CustomKey = "F13";
            cfg.Interval = 800;
            cfg.HoldPercent = 50;       // down 400 ms, up 400 ms, repeat
            Engine.Start(97, cfg, IntPtr.Zero, 0);

            // Record every edge for two cycles
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var edges = new List<double>();
            bool last = false;
            while (sw.Elapsed.TotalMilliseconds < 1900)
            {
                bool dn = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
                if (dn != last) { edges.Add(sw.Elapsed.TotalMilliseconds); last = dn; }
                Thread.Sleep(1);
            }
            Engine.Stop(97);
            Thread.Sleep(250);

            // edges: down, up, down, up... anchored at the start
            Check(edges.Count >= 4, "phase: saw at least two full presses ("
                + edges.Count + " edges)");
            if (edges.Count >= 4)
            {
                Check(edges[0] < 250,
                      "phase: pressed at once, not an interval late ("
                      + edges[0].ToString("0") + " ms)");
                double held = edges[1] - edges[0];
                Check(held > 250 && held < 550,
                      "phase: held its half of the interval (" + held.ToString("0") + " ms)");
                double idle = edges[2] - edges[1];
                Check(idle > 250 && idle < 550,
                      "phase: released for the other half (" + idle.ToString("0") + " ms)");
            }
        }

        // Export: the current cards as a profile INI plus every take they
        // reference, laid out the way %APPDATA%\Polyclicker is
        static void ExportTests()
        {
            Directory.CreateDirectory(AppConfig.MacroDir);
            File.WriteAllText(Path.Combine(AppConfig.MacroDir, "export-take.macro"),
                "# Polyclicker macro v1\r\n0.000 5 120 0\r\n");
            var cfg = new AppConfig();
            var s = new SlotConfig();
            s.Input = "Macro";
            s.Macro = "export-take.macro";
            s.Name = "Exported";
            cfg.Slots.Add(s);
            cfg.CurrentProfile = "Bundle";
            string zip = Path.Combine(Path.GetTempPath(), "polytest-export.zip");
            cfg.ExportBundle(zip);
            var names = new List<string>();
            using (var z = System.IO.Compression.ZipFile.OpenRead(zip))
                foreach (var e in z.Entries) names.Add(e.FullName.Replace('\\', '/'));
            Check(names.Contains("Profiles/Bundle.ini"), "export: profile ini in the bundle");
            Check(names.Contains("Macros/export-take.macro"),
                  "export: the referenced take in the bundle");
        }

        // A take holding a key for 2 ms would be invisible to a per-frame
        // consumer; playback's press floor must stretch the press itself to
        // ~10 ms without inflating anything else.
        static void PressFloorTests()
        {
            string p = Path.Combine(Path.GetTempPath(), "polytest-floor.macro");
            File.WriteAllLines(p, new string[]
            {
                "# Polyclicker macro v1",
                "0.000 3 124 0",
                "2.000 4 124 0",
            });
            var cfg = new SlotConfig();
            cfg.Input = "Macro";
            cfg.MacroLoop = false;
            cfg.Interval = 0;
            string warn;
            Engine.StartMacro(91, cfg, IntPtr.Zero, p, out warn);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double downAt = -1, upAt = -1;
            while (sw.Elapsed.TotalMilliseconds < 1500)
            {
                bool dn = (GetAsyncKeyState(0x7C) & 0x8000) != 0;
                if (dn && downAt < 0) downAt = sw.Elapsed.TotalMilliseconds;
                if (!dn && downAt >= 0) { upAt = sw.Elapsed.TotalMilliseconds; break; }
                Thread.Sleep(0);
            }
            double held = upAt - downAt;
            Check(downAt >= 0 && upAt >= 0, "floor take pressed and released");
            Check(held >= 7 && held <= 60,
                  "2 ms tap stretched to about a frame (held " + held.ToString("0.0") + " ms)");
        }

        static REv MkEv(double ms, byte type, int a, int b)
        {
            var e = new REv();
            e.Ms = ms; e.Type = type; e.A = a; e.B = b;
            return e;
        }

        static string MakeTake()
        {
            string p = Path.Combine(Path.GetTempPath(), "polytest-take.macro");
            File.WriteAllLines(p, new string[]
            {
                "# Polyclicker macro v1",
                "# window 100 100 800 600",
                "0.000 0 100 100",
                "10.000 0 200 150",
                "20.000 0 300 200",
                "520.000 1 0 0",
                "560.000 2 0 0",
                "600.000 3 65 0",
                "650.000 4 65 0",
                "700.000 3 16 0",
                "900.000 0 400 300",
                "1000.000 4 16 0",
                "1100.000 5 120 0",
                "1150.000 5 120 0",
                "2150.000 1 1 0",
                "2160.000 0 500 400",
                "2200.000 2 1 0",
            });
            return p;
        }

        static void TakeTests()
        {
            string p = MakeTake();
            var t = new TakeBuffer();
            t.Load(p);

            // grouping: move run, click, key tap, ONE row for the shift held
            // across a move (the move stays its own row), scroll burst, drag
            Check(t.Rows.Count == 7, "grouped into 7 rows (got " + t.Rows.Count + ")");
            Check(t.Rows[0].Kind == RKind.Move, "row0 move run");
            Check(t.Rows[1].Kind == RKind.Click && t.Rows[1].Desc == "Left click at 300, 200",
                  "row1 click desc (" + t.Rows[1].Desc + ")");
            Check(t.Rows[2].Kind == RKind.Key && t.Rows[2].Desc == "Key A", "row2 key tap");
            Check(t.Rows[3].Kind == RKind.Key && t.Rows[3].Desc.StartsWith("Key Shift")
                  && t.Rows[3].Desc.Contains("held across the next step")
                  && Math.Abs(t.Rows[3].EndMs - t.Rows[3].StartMs - 300) < 0.01,
                  "row3 held shift is one annotated row (" + t.Rows[3].Desc + ")");
            Check(t.Rows[1].X == 300 && t.Rows[1].Y == 200, "click row carries its spot");
            Check(t.Rows[4].Kind == RKind.Move, "row4 mid-hold move kept separate");
            Check(t.Rows[5].Kind == RKind.Scroll && t.Rows[5].Desc.EndsWith("×2"),
                  "row5 scroll burst (" + t.Rows[5].Desc + ")");
            Check(t.Rows[6].Kind == RKind.Drag, "row6 drag");
            Check(Math.Abs(t.GapOf(1) - 500) < 0.01, "gap before click is 500 ms");

            // deleting the held shift removes both ends but no time, because
            // the surviving move lives inside its span
            t.DeleteRows(new List<int> { 3 });
            Check(t.Rows.Count == 6, "shift row gone whole (rows: " + t.Rows.Count + ")");
            bool shiftLeft = false;
            for (int i = 0; i < t.EventCount; i++)
                if ((t.EventAt(i).Type == 3 || t.EventAt(i).Type == 4) && t.EventAt(i).A == 16)
                    shiftLeft = true;
            Check(!shiftLeft, "no orphaned shift events remain");
            Check(Math.Abs(t.TotalMs - 2200) < 0.01, "occupied span closes no time");
            Check(Math.Abs(t.Rows[3].StartMs - 900) < 0.01, "mid-hold move kept its time");
            Check(t.Undo() && t.Rows.Count == 7, "undo restores the pair");

            // deleting the held shift AND the move inside it closes the shift's
            // 300 ms once - the nested move span is not subtracted again
            t.DeleteRows(new List<int> { 3, 4 });
            Check(Math.Abs(t.TotalMs - 1900) < 0.01, "nested spans close once (total " + t.TotalMs + ")");
            Check(t.Undo(), "undo the nested delete");
            // a key held across its neighbour has no order to swap with it
            double keep = t.TotalMs;
            Check(t.MoveRows(new List<int> { 3 }, true) < 0, "no swap of a key held across its neighbour");
            Check(t.MoveRows(new List<int> { 4 }, false) < 0, "no swap up into a held key");
            Check(Math.Abs(t.TotalMs - keep) < 0.01, "refused swaps change nothing");

            // retiming: the click's 500 ms pause becomes 100 ms and everything
            // after slides back by 400
            t.SetGap(1, 100);
            Check(Math.Abs(t.Rows[1].StartMs - 120) < 0.01, "click retimed to 120 ms");
            Check(Math.Abs(t.TotalMs - 1800) < 0.01, "later events slid back 400 ms");
            Check(t.Undo(), "undo the retime");

            // step length: the click's 40 ms hold becomes 100 ms and later
            // events make room for the extra 60
            t.SetLength(1, 100);
            Check(Math.Abs(t.Rows[1].EndMs - t.Rows[1].StartMs - 100) < 0.01,
                  "click hold stretched to 100 ms");
            Check(Math.Abs(t.Rows[2].StartMs - 660) < 0.01,
                  "key tap pushed to 660 (got " + t.Rows[2].StartMs + ")");
            Check(Math.Abs(t.TotalMs - 2260) < 0.01, "take grew by 60 ms");
            Check(t.Undo(), "undo the click stretch");

            // a move run scales all its samples across the new length
            t.SetLength(0, 200);
            Check(Math.Abs(t.EventAt(1).Ms - 100) < 0.01, "middle sample scaled to 100");
            Check(Math.Abs(t.Rows[1].StartMs - 700) < 0.01, "click pushed to 700");
            Check(t.Undo(), "undo the move stretch");

            // inserting a step: a key press lands 100 ms after the click and
            // pushes what followed
            double at = t.InsertStep(1, new REv[]
            {
                MkEv(0, 3, 66, 0), MkEv(50, 4, 66, 0)
            }, 100);
            Check(Math.Abs(at - 660) < 0.01, "step landed at 660 (got " + at + ")");
            int nr = t.FindRowAt(at);
            Check(t.Rows[nr].Desc == "Key B", "inserted row reads Key B (" + t.Rows[nr].Desc + ")");
            Check(Math.Abs(t.Rows[nr + 1].StartMs - 750) < 0.01,
                  "key A pushed to 750 (got " + t.Rows[nr + 1].StartMs + ")");
            Check(Math.Abs(t.TotalMs - 2350) < 0.01, "take grew by gap plus step");
            Check(t.Undo(), "undo the insert");

            // deleting a whole click row closes exactly its own 40 ms
            t.DeleteRows(new List<int> { 1 });
            Check(Math.Abs(t.Rows[1].StartMs - 560) < 0.01,
                  "key tap slid from 600 to 560 (got " + t.Rows[1].StartMs + ")");
            Check(t.Undo(), "undo the click delete");

            // repointing a click inserts a move right before the press
            int before = t.EventCount;
            t.SetClickSpot(1, 999, 888);
            Check(t.EventCount == before + 1, "spot insert adds one event");
            Check(t.Rows[1].Desc == "Left click at 999, 888",
                  "click repointed (" + t.Rows[1].Desc + ")");
            Check(t.Undo(), "undo the repoint");

            // bend the opening move run to a new endpoint: the path anchors
            // at its start, scales toward the landing spot, and the click
            // that follows inherits it
            t.SetCoords(0, 0, 0, 500, 400);
            Check(t.Rows[0].Desc == "Move to 500, 400" && t.Rows[0].EX == 500,
                  "move run bent (" + t.Rows[0].Desc + ")");
            Check(t.EventAt(0).A == 167 && t.EventAt(1).A == 333,
                  "path scales toward the new endpoint ("
                  + t.EventAt(0).A + ", " + t.EventAt(1).A + ")");
            Check(t.Rows[1].Desc == "Left click at 500, 400",
                  "the click follows the pointer (" + t.Rows[1].Desc + ")");
            Check(t.Undo(), "undo the bend");

            // slide a drag whole: one delta on both ends keeps its shape
            t.SetCoords(6, 500, 400, 600, 500);
            TakeRow drag = t.Rows[t.Rows.Count - 1];
            Check(drag.Desc == "Right drag 500, 400 → 600, 500",
                  "drag slid whole (" + drag.Desc + ")");
            Check(t.Undo(), "undo the drag slide");

            // clipboard: extract to text, parse back, insert - a duplicate of
            // the click lands after it and pushes the rest
            string clip = t.ExtractText(new List<int> { 1 });
            REv[] parsed = TakeBuffer.ParseSteps(clip);
            Check(parsed != null && parsed.Length == 2 && parsed[0].Ms == 0,
                  "clipboard text round-trips " + (parsed == null ? 0 : parsed.Length) + " events");
            double pAt = t.InsertStep(1, parsed, 100);
            Check(t.Rows[2].Kind == RKind.Click && Math.Abs(t.Rows[2].StartMs - 660) < 0.01,
                  "pasted click lands at 660 (" + t.Rows[2].StartMs + ")");
            Check(Math.Abs(t.TotalMs - 2340) < 0.01, "paste grew the take by 140");
            Check(t.Undo(), "undo the paste");

            // move: the key tap (row 2) hops over the click above it, and one
            // undo puts it back
            double mAt = t.MoveRows(new List<int> { 2 }, false);
            Check(mAt >= 0 && t.Rows[1].Kind == RKind.Key && t.Rows[2].Kind == RKind.Click,
                  "key tap moved above the click");
            int depth = t.UndoDepth;
            Check(t.Undo() && t.Rows[1].Kind == RKind.Click && t.Rows[2].Kind == RKind.Key
                  && t.UndoDepth == depth - 1,
                  "one undo reverses the whole move");

            // moving the top row up (or a gappy selection) is refused
            Check(t.MoveRows(new List<int> { 0 }, false) < 0, "no move above the top");
            Check(t.MoveRows(new List<int> { 0, 2 }, true) < 0, "no move of a gappy selection");

            // insert before everything: the new step owns time zero
            double zAt = t.InsertStep(-1, new REv[] { MkEv(0, 5, 120, 0) }, 100);
            Check(zAt == 0 && t.Rows[0].Kind == RKind.Scroll,
                  "start-insert put a scroll at time zero");
            Check(Math.Abs(t.Rows[1].StartMs - 100) < 0.01,
                  "old first move slid to 100 (" + t.Rows[1].StartMs + ")");
            Check(t.Undo(), "undo the start-insert");

            // committing a cell with its own value is not an edit: no undo
            // entry, and the fingerprint the dirty check compares is unchanged
            int depth0 = t.UndoDepth;
            string print0 = t.Fingerprint();
            t.SetGap(1, (int)Math.Round(t.GapOf(1)));
            t.SetLength(1, (int)Math.Round(t.Rows[1].EndMs - t.Rows[1].StartMs));
            t.SetClickSpot(1, t.Rows[1].X, t.Rows[1].Y);
            t.SetCoords(0, 0, 0, t.Rows[0].EX, t.Rows[0].EY);
            Check(t.UndoDepth == depth0 && t.Fingerprint() == print0,
                  "no-op edits leave no trace");

            // round trip: header (window info included) and events survive,
            // and the engine's own loader still accepts the file
            string p2 = Path.Combine(Path.GetTempPath(), "polytest-take2.macro");
            t.WriteTo(p2, 0);
            var t2 = new TakeBuffer();
            t2.Load(p2);
            Check(t2.EventCount == t.EventCount, "round trip keeps every event");
            Check(t2.Header.Contains("# window 100 100 800 600"), "window header survives");
            MacroFile.WindowInfo win;
            Ev[] loaded = MacroFile.Load(p2, 10000000, out win);
            Check(loaded != null && loaded.Length == t.EventCount && win.Valid,
                  "engine loader accepts the edited file");
        }

        [STAThread]
        static void Main()
        {
            Engine.Startup();

            TakeTests();
            HoldForeverTests();
            HoldResumeTests();
            HoldPhaseTests();
            PressFloorTests();
            ExportTests();

            // --- StartAt parsing ------------------------------------------
            int hh, mm;
            Check(SlotConfig.TryParseStartAt("7:05", out hh, out mm) && hh == 7 && mm == 5, "parse 7:05");
            Check(SlotConfig.TryParseStartAt("23:59", out hh, out mm) && hh == 23 && mm == 59, "parse 23:59");
            Check(!SlotConfig.TryParseStartAt("25:00", out hh, out mm), "reject 25:00");
            Check(!SlotConfig.TryParseStartAt("0700", out hh, out mm), "reject 0700 (no colon)");
            Check(!SlotConfig.TryParseStartAt("7", out hh, out mm), "reject bare 7");
            Check(!SlotConfig.TryParseStartAt("12:60", out hh, out mm), "reject 12:60");

            // --- delay counts down, then clicks, then the limit stops it ---
            var cfg = new SlotConfig();
            cfg.Input = "Custom Key";
            cfg.CustomKey = "F15";          // no standard binding anywhere
            cfg.Interval = 50;
            cfg.StopClicks = 2;
            cfg.StartDelaySec = 2;
            Engine.Start(0, cfg, IntPtr.Zero, 0);

            int pending = Engine.PendingSeconds(0);
            Check(pending >= 1 && pending <= 2, "pending ~2s right after start (got " + pending + ")");
            Check(Engine.IsRunning(0), "running while counting down");
            Thread.Sleep(1000);
            Check(Engine.ClickCount(0) == 0, "no clicks 1s into a 2s delay");
            Check(Engine.PendingSeconds(0) >= 0, "still pending at 1s");
            Thread.Sleep(1600);             // t = 2.6s: begun, 2 clicks, stopped
            Check(Engine.PendingSeconds(0) == -1, "not pending after the delay");
            Thread.Sleep(400);
            Check(!Engine.IsRunning(0), "stopped by its click limit");

            // --- cancelling mid-countdown never clicks ---------------------
            var cfg2 = new SlotConfig();
            cfg2.Input = "Custom Key";
            cfg2.CustomKey = "F15";
            cfg2.Interval = 50;
            cfg2.StartDelaySec = 5;
            Engine.Start(1, cfg2, IntPtr.Zero, 0);
            Thread.Sleep(500);
            Check(Engine.PendingSeconds(1) >= 3, "second slot counting down");
            Engine.Stop(1);
            Thread.Sleep(400);              // worker notices within a slice
            Check(!Engine.IsRunning(1), "cancelled during the wait");
            Check(Engine.ClickCount(1) == 0, "no click from a cancelled wait");

            Engine.Shutdown();
            Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILURES");
            Environment.Exit(fails == 0 ? 0 : 1);
        }
    }
}
