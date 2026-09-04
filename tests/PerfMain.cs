// Resize cost with real data: a dozen mixed cards, then a simulated frame
// drag - one WM_SIZE per pixel, like a mouse at 1 kHz - and the CPU time
// each tick costs. Run before and after a change to compare.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Polyclicker
{
    static class PerfMain
    {
        static void Pump() { Application.DoEvents(); }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            var f = new MainForm();
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(40, 40);
            f.ClientSize = new Size(700, 900);
            f.Show();
            for (int i = 0; i < 20; i++) { Pump(); System.Threading.Thread.Sleep(30); }

            var sf = typeof(MainForm).GetField("surface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var surface = (CardSurface)sf.GetValue(f);
            Console.WriteLine("cards: " + surface.Count);
            var proc = Process.GetCurrentProcess();
            int ticks = 300;
            CardSurface.Interactive = true;      // what WM_ENTERSIZEMOVE sets during a real drag
            // warm
            for (int i = 0; i < 20; i++) { f.ClientSize = new Size(700 + i, 900); Pump(); }
            CardSurface.PaintCount = 0; CardSurface.PaintTicks = 0; CardSurface.LayoutTicks = 0; CardSurface.PhaseCardsTicks = CardSurface.PhaseBoxTicks = CardSurface.PhaseGlyphTicks = CardSurface.PhaseTextTicks = 0;
            MainForm.ResizeTicks = 0; MainForm.ResizeBaseTicks = 0; MainForm.ResizeStripTicks = 0; MainForm.ResizeSurfaceTicks = 0; MainForm.ResizeStripBoundsTicks = 0; MainForm.ResizeGearTicks = 0;
            TimeSpan cpu0 = proc.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++)
            {
                f.ClientSize = new Size(720 + (i % 200), 900);
                Pump();
            }
            sw.Stop();
            CardSurface.Interactive = false;
            proc.Refresh();
            TimeSpan cpu = proc.TotalProcessorTime - cpu0;
            Console.WriteLine("drag: " + ticks + " ticks, wall " + sw.ElapsedMilliseconds
                + " ms, cpu " + cpu.TotalMilliseconds.ToString("0") + " ms  ("
                + (cpu.TotalMilliseconds / ticks).ToString("0.00") + " ms cpu/tick)");

            Console.WriteLine("  phases: cards(body+grip+layout) " + (CardSurface.PhaseCardsTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms, boxes " + (CardSurface.PhaseBoxTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms, glyphs " + (CardSurface.PhaseGlyphTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms, text " + (CardSurface.PhaseTextTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms");
            if (CardSurface.PaintCount == 0) Console.WriteLine("  !! no surface paints: the resize never reached the surface");
            Console.WriteLine("surface paints: " + CardSurface.PaintCount
                + ", paint " + (CardSurface.PaintTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms"
                + " (of which layout " + (CardSurface.LayoutTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms)");
            Console.WriteLine("  base.OnResize " + (MainForm.ResizeBaseTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms, panes+chips (wall) " + (MainForm.ResizeStripTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms (surface " + (MainForm.ResizeSurfaceTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + ", strip " + (MainForm.ResizeStripBoundsTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + ", gear " + (MainForm.ResizeGearTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + ")");
            Console.WriteLine("mainform OnResize " + (MainForm.ResizeTicks * 1000.0 / Stopwatch.Frequency).ToString("0") + " ms");

            // The floor: an empty double-buffered Form of the same size
            var bare = new Form(); bare.StartPosition = FormStartPosition.Manual;
            bare.Location = new Point(40, 40); bare.ClientSize = new Size(700, 900);
            typeof(Form).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(bare, true, null);
            bare.Show(); for (int i = 0; i < 10; i++) { Pump(); System.Threading.Thread.Sleep(30); }
            proc.Refresh(); cpu0 = proc.TotalProcessorTime; sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) { bare.ClientSize = new Size(720 + (i % 200), 900); Pump(); }
            sw.Stop(); proc.Refresh(); cpu = proc.TotalProcessorTime - cpu0;
            Console.WriteLine("bare form: " + (cpu.TotalMilliseconds / ticks).ToString("0.00") + " ms cpu/tick, wall " + sw.ElapsedMilliseconds + " ms");
            bare.Close();
            // and the same form without its double buffer
            var bare2 = new Form(); bare2.StartPosition = FormStartPosition.Manual;
            bare2.Location = new Point(40, 40); bare2.ClientSize = new Size(700, 900);
            bare2.Show(); for (int i = 0; i < 10; i++) { Pump(); System.Threading.Thread.Sleep(30); }
            proc.Refresh(); cpu0 = proc.TotalProcessorTime; sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) { bare2.ClientSize = new Size(720 + (i % 200), 900); Pump(); }
            sw.Stop(); proc.Refresh(); cpu = proc.TotalProcessorTime - cpu0;
            Console.WriteLine("bare form, no double buffer: " + (cpu.TotalMilliseconds / ticks).ToString("0.00") + " ms cpu/tick");
            bare2.Close();

            // settle + one crisp paint
            for (int i = 0; i < 10; i++) { Pump(); System.Threading.Thread.Sleep(20); }
            f.Close();

            // --- runtime: what a running clicker and a dense macro cost ---
            Engine.Startup();
            var cfg = new SlotConfig();
            cfg.Input = "Custom Key"; cfg.CustomKey = "F13"; cfg.Interval = 20;   // 50 cps
            proc.Refresh(); cpu0 = proc.TotalProcessorTime;
            Engine.Start(0, cfg, IntPtr.Zero, 0);
            System.Threading.Thread.Sleep(3000);
            Engine.Stop(0);
            proc.Refresh(); cpu = proc.TotalProcessorTime - cpu0;
            Console.WriteLine("clicker at 50 cps for 3 s: " + cpu.TotalMilliseconds.ToString("0") + " ms cpu = "
                + (cpu.TotalMilliseconds / 3000 * 100).ToString("0.0") + "% of a core");

            string take = Path.Combine(Path.GetTempPath(), "polyperf-take.macro");
            using (var w = new StreamWriter(take))
            {
                w.WriteLine("# Polyclicker macro v1");
                for (int i = 0; i < 3000; i++)      // a pointer sample every ms for 3 s
                    w.WriteLine(i + ".000 0 " + (200 + i % 400) + " " + (300 + (i * 7) % 300) + " 0");
            }
            var mcfg = new SlotConfig();
            mcfg.Input = "Macro"; mcfg.MacroLoop = false; mcfg.Interval = 0;
            string warn;
            proc.Refresh(); cpu0 = proc.TotalProcessorTime;
            Engine.StartMacro(1, mcfg, IntPtr.Zero, take, out warn);
            System.Threading.Thread.Sleep(3300);
            proc.Refresh(); cpu = proc.TotalProcessorTime - cpu0;
            Console.WriteLine("macro, 1000 pointer samples/s for 3 s: " + cpu.TotalMilliseconds.ToString("0") + " ms cpu = "
                + (cpu.TotalMilliseconds / 3000 * 100).ToString("0.0") + "% of a core");
        }
    }
}
