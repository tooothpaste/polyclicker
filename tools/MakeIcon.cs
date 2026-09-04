// Renders polyclicker.svg to a multi-size .ico. The SVG is a fixed shape -
// groups carrying a matrix transform around one circle or one cubic-Bezier
// path with an rgb() fill - so this reads exactly that and nothing more;
// no SVG library ships with Windows. With -dev the petals are muted and a
// terminal badge sits in the bottom-right corner: the dev build's icon.
//
//   MakeIcon.exe polyclicker.svg app-dev.ico -dev
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

static class MakeIcon
{
    sealed class Shape
    {
        public Matrix M;
        public Color Fill;
        public GraphicsPath Path;       // in the group's own units
    }

    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("MakeIcon <svg> <ico> [-dev]"); return 2; }
        bool dev = args.Length > 2 && args[2] == "-dev";
        float viewBox;
        List<Shape> shapes = Load(args[0], out viewBox);

        var frames = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap bmp = Render(shapes, viewBox, size, dev))
                frames.Add(size >= 256 ? Png(bmp) : Dib(bmp));
        }
        WriteIco(args[1], frames);
        Console.WriteLine("wrote " + args[1] + " (" + Sizes.Length + " sizes" + (dev ? ", dev" : "") + ")");
        return 0;
    }

    // --- the SVG ----------------------------------------------------------------

    static List<Shape> Load(string file, out float viewBox)
    {
        var doc = new XmlDocument();
        doc.Load(file);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("s", "http://www.w3.org/2000/svg");
        string vb = doc.DocumentElement.GetAttribute("viewBox");
        viewBox = float.Parse(vb.Split(' ')[2], CultureInfo.InvariantCulture);

        var shapes = new List<Shape>();
        foreach (XmlElement g in doc.DocumentElement.SelectNodes("s:g", ns))
        {
            var sh = new Shape();
            sh.M = ParseMatrix(g.GetAttribute("transform"));
            XmlElement el = (XmlElement)(g.SelectSingleNode("s:circle", ns) ?? g.SelectSingleNode("s:path", ns));
            if (el == null) continue;
            sh.Fill = ParseFill(el.GetAttribute("style"));
            if (sh.Fill == Color.White) continue;     // the backing disc; the icon sits on transparency
            sh.Path = new GraphicsPath();
            if (el.Name == "circle")
            {
                float cx = F(el.GetAttribute("cx")), cy = F(el.GetAttribute("cy")), r = F(el.GetAttribute("r"));
                sh.Path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            }
            else AddPathData(sh.Path, el.GetAttribute("d"));
            shapes.Add(sh);
        }
        return shapes;
    }

    static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

    static Matrix ParseMatrix(string t)
    {
        if (string.IsNullOrEmpty(t)) return new Matrix();
        Match m = Regex.Match(t, @"matrix\(([^)]*)\)");
        string[] p = m.Groups[1].Value.Split(',');
        return new Matrix(F(p[0]), F(p[1]), F(p[2]), F(p[3]), F(p[4]), F(p[5]));
    }

    static Color ParseFill(string style)
    {
        Match m = Regex.Match(style, @"fill:\s*rgb\((\d+),(\d+),(\d+)\)");
        if (m.Success) return Color.FromArgb(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        if (style.Contains("fill:white")) return Color.White;
        return Color.Black;
    }

    // M x,y  C x1,y1 x2,y2 x,y ...  Z - absolute commands only, which is all
    // the file uses
    static void AddPathData(GraphicsPath path, string d)
    {
        var nums = new List<float>();
        PointF cur = PointF.Empty, start = PointF.Empty;
        char cmd = ' ';
        foreach (Match tok in Regex.Matches(d, @"[MCZ]|-?\d*\.?\d+"))
        {
            string s = tok.Value;
            if (s == "M" || s == "C" || s == "Z")
            {
                if (s == "Z") { path.CloseFigure(); cur = start; }
                cmd = s[0];
                nums.Clear();
                continue;
            }
            nums.Add(F(s));
            if (cmd == 'M' && nums.Count == 2)
            {
                cur = start = new PointF(nums[0], nums[1]);
                path.StartFigure();
                nums.Clear();
            }
            else if (cmd == 'C' && nums.Count == 6)
            {
                var c1 = new PointF(nums[0], nums[1]);
                var c2 = new PointF(nums[2], nums[3]);
                var end = new PointF(nums[4], nums[5]);
                path.AddBezier(cur, c1, c2, end);
                cur = end;
                nums.Clear();
            }
        }
    }

    // --- drawing ----------------------------------------------------------------

    static Bitmap Render(List<Shape> shapes, float viewBox, int size, bool dev)
    {
        // Supersampled, then shrunk: GDI+ antialiasing alone leaves the
        // small sizes ragged
        int over = size <= 64 ? 8 : 4;
        int big = size * over;
        var hi = new Bitmap(big, big, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(hi))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float k = big / viewBox;
            var scale = new Matrix(k, 0, 0, k, 0, 0);
            foreach (Shape sh in shapes)
            {
                var m = sh.M.Clone();
                m.Multiply(scale, MatrixOrder.Append);
                g.Transform = m;
                Color c = dev ? Mute(sh.Fill) : sh.Fill;
                using (var b = new SolidBrush(c)) g.FillPath(b, sh.Path);
            }
            g.ResetTransform();
            if (dev) Badge(g, big);
        }
        var outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(outBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(hi, new Rectangle(0, 0, size, size), 0, 0, big, big, GraphicsUnit.Pixel);
        }
        hi.Dispose();
        return outBmp;
    }

    // Petals lose most of their saturation and drift toward a mid gray;
    // the white disc and the gray centre are left alone
    static Color Mute(Color c)
    {
        if (c.R > 180 && c.G > 180 && c.B > 180) return c;
        float lum = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        Func<byte, int> f = delegate(byte v)
        {
            float s = lum + (v - lum) * 0.34f;        // keep 34% of the saturation
            s = s * 0.70f + 215 * 0.30f;              // and lift toward light gray
            return Math.Max(0, Math.Min(255, (int)Math.Round(s)));
        };
        return Color.FromArgb(f(c.R), f(c.G), f(c.B));
    }

    // A terminal prompt drawn the way the app draws its glyphs: gray strokes
    // on the icon's transparency, bottom-right
    static void Badge(Graphics g, int big)
    {
        float b = big * 0.44f;
        float x = big - b - big * 0.01f, y = big - b - big * 0.01f;
                // No chip behind it: the strokes sit on the icon's transparency, with a
        // faint white halo so they still read where they cross a petal
        using (var halo = new Pen(Color.FromArgb(150, 255, 255, 255), b * 0.20f))
        {
            halo.StartCap = halo.EndCap = LineCap.Round;
            halo.LineJoin = LineJoin.Round;
            g.DrawLines(halo, Chevron(x, y, b));
            g.DrawLine(halo, x + b * 0.52f, y + b * 0.70f, x + b * 0.78f, y + b * 0.70f);
        }
        using (var pen = new Pen(Color.FromArgb(96, 96, 106), b * 0.10f))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, Chevron(x, y, b));
            g.DrawLine(pen, x + b * 0.52f, y + b * 0.70f, x + b * 0.78f, y + b * 0.70f);
        }
    }

    static PointF[] Chevron(float x, float y, float b)
    {
        return new[]
        {
            new PointF(x + b * 0.24f, y + b * 0.32f),
            new PointF(x + b * 0.44f, y + b * 0.50f),
            new PointF(x + b * 0.24f, y + b * 0.68f),
        };
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // --- the .ico container. The 256 frame is PNG-compressed, as Windows
    // expects; the rest are plain 32-bit DIBs, because the .NET Icon class
    // that reads the icon back at runtime (title bar, tray) can't decode a
    // PNG frame at those sizes -------------------------------------------

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // BITMAPINFOHEADER + bottom-up BGRA rows + an empty 1-bit AND mask
    static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskRow = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskRow * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                    bw.Write(bmp.GetPixel(x, y).ToArgb());
            bw.Write(new byte[maskRow * h]);
            return ms.ToArray();
        }
    }


    static void WriteIco(string file, List<byte[]> frames)
    {
        using (var fs = new FileStream(file, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
            int offset = 6 + 16 * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                int s = Sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) w.Write(f);
        }
    }
}
