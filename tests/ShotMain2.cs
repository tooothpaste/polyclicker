// Screenshots the real MainForm (data dir prepared by the caller) so the
// card rows can be eyeballed.
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polyclicker
{
    static class ShotMain2
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            // POLYCLICKER_UISCALE=150 renders everything at 150% - the way to
            // see a high-DPI layout on a 100% display
            string sc = Environment.GetEnvironmentVariable("POLYCLICKER_UISCALE");
            int pct;
            if (int.TryParse(sc, out pct)) Theme.UserScale = pct / 100f;
            string sfx = pct > 0 && pct != 100 ? "-" + pct : "";
            var f = new MainForm();
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(60, 60);
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
            var r = f.Bounds;
            using (var bmp = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                bmp.Save(Path.Combine(args[0], "main-card" + sfx + ".png"));
            }

            // The live path: pretend the window just landed on a 150% monitor.
            // WM_DPICHANGED with 144 dpi and a suggested rect 1.5x the current
            // one is exactly what Windows sends; the window must re-lay itself
            // out crisply at the new scale, not stretch.
            if (sfx.Length == 0)
            {
                var rc = new RECT();
                rc.L = r.X; rc.T = r.Y;
                rc.R = r.X + (int)(r.Width * 1.5); rc.B = r.Y + (int)(r.Height * 1.5);
                IntPtr pr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(RECT)));
                Marshal.StructureToPtr(rc, pr, false);
                SendMessage(f.Handle, 0x02E0, (IntPtr)((144 << 16) | 144), pr);
                Marshal.FreeHGlobal(pr);
                for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                var r2 = f.Bounds;
                using (var bmp = new Bitmap(r2.Width, r2.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(r2.Location, Point.Empty, r2.Size);
                    bmp.Save(Path.Combine(args[0], "main-card-dpichange.png"));
                }
                // And zoom on top of that: Ctrl+- twice from 100 lands on 80
                f.ZoomStep(-1); f.ZoomStep(-1);
                for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                var r3 = f.Bounds;
                using (var bmp = new Bitmap(r3.Width, r3.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(r3.Location, Point.Empty, r3.Size);
                    bmp.Save(Path.Combine(args[0], "main-card-zoom80.png"));
                }
            }
            f.Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
