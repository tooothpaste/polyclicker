// Opens the macro editor on a synthetic take and screenshots it, light and
// dark, so the layout can be eyeballed without driving the whole app.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Polyclicker
{
    static class ShotMain
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
            string take = args[0], outDir = args[1];
            foreach (bool dark in new[] { false, true })
            {
                Theme.Dark = dark;
                using (var d = new MacroEditorDialog(take, "Test take", null))
                {
                    d.StartPosition = FormStartPosition.Manual;
                    d.Location = new Point(60, 60);
                    d.Show();
                    for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                    // light theme also opens a position cell - the edited
                    // cell must blank under its editor box
                    if (!dark)
                    {
                        var begin = typeof(MacroEditorDialog).GetMethod("BeginCellEdit",
                            System.Reflection.BindingFlags.NonPublic
                          | System.Reflection.BindingFlags.Instance);
                        var cellT = typeof(MacroEditorDialog).GetNestedType("Cell",
                            System.Reflection.BindingFlags.NonPublic);
                        begin.Invoke(d, new object[] { 1, Enum.ToObject(cellT, 2) });
                        for (int i = 0; i < 8; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                    }
                    var r = d.Bounds;
                    using (var bmp = new Bitmap(r.Width, r.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        bmp.Save(Path.Combine(outDir, (dark ? "editor-dark" : "editor-light") + sfx + ".png"));
                    }
                    d.Close();
                }
            }
        }
    }
}
