// Screenshots the Settings dialog and a macro card's Advanced dialog, so
// their layouts can be eyeballed after a change.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Polyclicker
{
    static class ShotMain3
    {
        static void Shoot(Form d, string file)
        {
            d.StartPosition = FormStartPosition.Manual;
            d.Location = new Point(60, 60);
            d.Show();
            for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
            var r = d.Bounds;
            using (var bmp = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                bmp.Save(file);
            }
            d.Close();
        }

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
            Shoot(new SettingsDialog(new AppConfig(), "", null),
                  Path.Combine(args[0], "settings" + sfx + ".png"));
            var slot = new SlotConfig();
            slot.Input = "Macro";
            slot.Macro = "Take.macro";
            Shoot(new AdvancedDialog(slot, "Macro card"),
                  Path.Combine(args[0], "advanced-macro" + sfx + ".png"));
            Shoot(new ConfirmDialog("Remove auto-clicker",
                      "Remove Clicker?\n\nIts settings are discarded. Any recorded macro it used stays in the Macros folder.",
                      "Remove", true),
                  Path.Combine(args[0], "confirm" + sfx + ".png"));
        }
    }
}
