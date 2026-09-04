// Screenshots the main window's client area for the README - no title bar,
// rounded corners, a hairline border - against whatever data folder the
// caller prepared (readme-shots.ps1 writes the sample profile).
//
//   ShotReadme.exe <out.png>
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Polyclicker
{
    static class ShotReadme
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            var f = new MainForm();
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(60, 60);
            f.TopMost = true;               // launched from a script, it would open behind
            f.Show();
            f.Activate();
            // Long enough for the window-gate check to have run, so a gated
            // card shows its "isn't open" line
            for (int i = 0; i < 50; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }

            Point at = f.PointToScreen(Point.Empty);
            Size sz = f.ClientSize;
            using (var raw = new Bitmap(sz.Width, sz.Height))
            using (var outBmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(raw))
                    g.CopyFromScreen(at, Point.Empty, sz);
                using (var g = Graphics.FromImage(outBmp))
                using (var path = Theme.RoundPath(new Rectangle(0, 0, sz.Width - 1, sz.Height - 1), 10))
                using (var edge = new Pen(Theme.Dark ? Color.FromArgb(72, 72, 80) : Color.FromArgb(196, 196, 204)))
                {
                    g.Clear(Color.Transparent);
                    g.SetClip(path);
                    g.DrawImageUnscaled(raw, 0, 0);
                    g.ResetClip();
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPath(edge, path);
                }
                outBmp.Save(args[0]);
            }
            f.Close();
        }
    }
}
