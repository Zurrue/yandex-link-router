using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Generates assets/icon.ico, the variant sheet and the GitHub social preview.
// Usage: make-assets.exe <repo root> [variant letter a..f]
internal static class MakeAssets
{
    private static readonly int[] IconSizes = { 16, 24, 32, 48, 256 };

    private enum Variant { Note = 0, Double = 1, Letter = 2, Play = 3, Dark = 4, Square = 5 }

    private static readonly string[] VariantNames =
    {
        "A. Note (current)",
        "B. Double note",
        "C. Letter Ya",
        "D. Play",
        "E. Dark",
        "F. Rounded square"
    };

    private sealed class Frame
    {
        public int Size;
        public byte[] Data;
    }

    private static void Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        Variant current = args.Length > 1 ? ParseVariant(args[1]) : Variant.Note;

        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);

        string ico = Path.Combine(assets, "icon.ico");
        WriteIcon(ico, IconSizes, current);

        string sheet = Path.Combine(assets, "icon-variants.png");
        WriteVariantSheet(sheet);

        string preview = Path.Combine(assets, "icon-preview.png");
        WritePreviewSheet(preview, current);

        string social = Path.Combine(assets, "social-preview.png");
        WriteSocial(social, 1280, 640, current);

        Console.WriteLine("icon.ico            " + new FileInfo(ico).Length + " bytes");
        Console.WriteLine("icon-variants.png   " + new FileInfo(sheet).Length + " bytes");
        Console.WriteLine("icon-preview.png    " + new FileInfo(preview).Length + " bytes");
        Console.WriteLine("social-preview.png  " + new FileInfo(social).Length + " bytes");
    }

    private static Variant ParseVariant(string value)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "b":
            case "double":
                return Variant.Double;
            case "c":
            case "letter":
            case "ya":
                return Variant.Letter;
            case "d":
            case "play":
                return Variant.Play;
            case "e":
            case "dark":
                return Variant.Dark;
            case "f":
            case "square":
                return Variant.Square;
            default:
                return Variant.Note;
        }
    }

    // ---------- icon ----------

    private static void WriteIcon(string path, int[] sizes, Variant variant)
    {
        var frames = new Frame[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            using (Bitmap bmp = RenderIcon(size, variant))
            {
                frames[i] = new Frame
                {
                    Size = size,
                    Data = size >= 48 ? EncodePng(bmp) : EncodeDib(bmp)
                };
            }
        }

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0);          // reserved
            w.Write((short)1);          // type: icon
            w.Write((short)frames.Length);

            int offset = 6 + 16 * frames.Length;

            foreach (Frame frame in frames)
            {
                byte dim = (byte)(frame.Size >= 256 ? 0 : frame.Size);
                w.Write(dim);           // width
                w.Write(dim);           // height
                w.Write((byte)0);       // palette
                w.Write((byte)0);       // reserved
                w.Write((short)1);      // planes
                w.Write((short)32);     // bpp
                w.Write(frame.Data.Length);
                w.Write(offset);
                offset += frame.Data.Length;
            }

            foreach (Frame frame in frames)
            {
                w.Write(frame.Data);
            }
        }
    }

    private static Bitmap RenderIcon(int size, Variant variant)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float pad = Math.Max(0.5f, size * 0.02f);
            var rect = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
            var ink = Color.FromArgb(26, 26, 26);
            var yellow = Color.FromArgb(255, 219, 77);

            if (variant == Variant.Square)
            {
                var box = new RectangleF(size * 0.05f, size * 0.05f, size * 0.90f, size * 0.90f);

                using (GraphicsPath path = RoundedRect(box, size * 0.22f))
                using (var fill = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(size, size),
                    Color.FromArgb(255, 228, 92), Color.FromArgb(255, 208, 38)))
                {
                    g.FillPath(fill, path);
                    using (var pen = new Pen(Color.FromArgb(45, 0, 0, 0), Math.Max(0.7f, size * 0.015f)))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                DrawNote(g, size, ink, 1f);
            }
            else if (variant == Variant.Dark)
            {
                using (var fill = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(size, size),
                    Color.FromArgb(56, 58, 64), Color.FromArgb(20, 21, 25)))
                {
                    g.FillEllipse(fill, rect);
                }

                using (var pen = new Pen(yellow, Math.Max(0.8f, size * 0.055f)))
                {
                    g.DrawEllipse(pen, RectangleF.Inflate(rect, -size * 0.055f, -size * 0.055f));
                }

                DrawNote(g, size, yellow, 0.82f);
            }
            else
            {
                using (var fill = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(size, size),
                    Color.FromArgb(255, 228, 92), Color.FromArgb(255, 208, 38)))
                {
                    g.FillEllipse(fill, rect);
                }

                using (var pen = new Pen(Color.FromArgb(45, 0, 0, 0), Math.Max(0.7f, size * 0.015f)))
                {
                    g.DrawEllipse(pen, rect);
                }

                if (variant == Variant.Double)
                {
                    DrawDoubleNote(g, size, ink);
                }
                else if (variant == Variant.Letter)
                {
                    DrawLetter(g, size, ink);
                }
                else if (variant == Variant.Play)
                {
                    DrawPlay(g, size, ink);
                }
                else
                {
                    DrawNote(g, size, ink, 1f);
                }
            }
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF box, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawNote(Graphics g, float size, Color color, float scale)
    {
        GraphicsState state = g.Save();

        if (scale != 1f)
        {
            g.TranslateTransform(size / 2, size / 2);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-size / 2, -size / 2);
        }

        bool small = size <= 24;
        float cx = size * 0.40f;
        float cy = size * 0.68f;
        float headW = size * (small ? 0.42f : 0.34f);
        float headH = size * (small ? 0.32f : 0.25f);

        using (var brush = new SolidBrush(color))
        {
            GraphicsState head = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-22f);
            g.FillEllipse(brush, -headW / 2, -headH / 2, headW, headH);
            g.Restore(head);

            float stemX = cx + headW * 0.36f;
            float stemW = Math.Max(small ? 2f : 1.4f, size * (small ? 0.11f : 0.075f));
            float stemTop = size * 0.24f;
            float stemBottom = cy + headH * 0.10f;

            g.FillRectangle(brush, stemX, stemTop, stemW, stemBottom - stemTop);

            using (var flag = new GraphicsPath())
            {
                flag.AddBezier(
                    stemX + stemW * 0.5f, stemTop,
                    stemX + size * 0.26f, stemTop + size * 0.02f,
                    stemX + size * 0.24f, stemTop + size * 0.20f,
                    stemX + stemW * 0.5f, stemTop + size * 0.24f);
                flag.AddLine(
                    stemX + stemW * 0.5f, stemTop + size * 0.24f,
                    stemX + stemW * 0.5f, stemTop + size * 0.08f);
                flag.CloseFigure();
                g.FillPath(brush, flag);
            }
        }

        g.Restore(state);
    }

    private static void DrawDoubleNote(Graphics g, float size, Color color)
    {
        bool small = size <= 24;
        float headW = size * (small ? 0.30f : 0.25f);
        float headH = size * (small ? 0.22f : 0.18f);
        float cx1 = size * 0.31f;
        float cy1 = size * 0.73f;
        float cx2 = size * 0.60f;
        float cy2 = size * 0.67f;
        float stemW = Math.Max(small ? 2f : 1.4f, size * (small ? 0.10f : 0.07f));
        float top1 = size * 0.27f;
        float top2 = size * 0.21f;
        float beamH = size * (small ? 0.13f : 0.10f);

        using (var brush = new SolidBrush(color))
        {
            GraphicsState s1 = g.Save();
            g.TranslateTransform(cx1, cy1);
            g.RotateTransform(-22f);
            g.FillEllipse(brush, -headW / 2, -headH / 2, headW, headH);
            g.Restore(s1);

            GraphicsState s2 = g.Save();
            g.TranslateTransform(cx2, cy2);
            g.RotateTransform(-22f);
            g.FillEllipse(brush, -headW / 2, -headH / 2, headW, headH);
            g.Restore(s2);

            float x1 = cx1 + headW * 0.38f;
            float x2 = cx2 + headW * 0.38f;

            g.FillRectangle(brush, x1, top1, stemW, cy1 + headH * 0.10f - top1);
            g.FillRectangle(brush, x2, top2, stemW, cy2 + headH * 0.10f - top2);

            var beam = new[]
            {
                new PointF(x1, top1),
                new PointF(x2 + stemW, top2),
                new PointF(x2 + stemW, top2 + beamH),
                new PointF(x1, top1 + beamH)
            };

            g.FillPolygon(brush, beam);
        }
    }

    private static void DrawLetter(Graphics g, float size, Color color)
    {
        using (var font = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString("\u042F", font, brush, new RectangleF(0, -size * 0.02f, size, size), format);
        }
    }

    private static void DrawPlay(Graphics g, float size, Color color)
    {
        var triangle = new[]
        {
            new PointF(size * 0.38f, size * 0.29f),
            new PointF(size * 0.77f, size * 0.50f),
            new PointF(size * 0.38f, size * 0.71f)
        };

        using (var brush = new SolidBrush(color))
        using (var pen = new Pen(color, Math.Max(1f, size * 0.07f)))
        {
            pen.LineJoin = LineJoin.Round;
            g.FillPolygon(brush, triangle);
            g.DrawPolygon(pen, triangle);
        }
    }

    // 32-bit DIB frame with an all-zero AND mask (alpha channel does the work).
    private static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        int xorSize = w * h * 4;
        int maskStride = ((w + 31) / 32) * 4;
        int maskSize = maskStride * h;

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40);            // biSize
            bw.Write(w);             // biWidth
            bw.Write(h * 2);         // biHeight (XOR + AND)
            bw.Write((short)1);      // biPlanes
            bw.Write((short)32);     // biBitCount
            bw.Write(0);             // biCompression
            bw.Write(xorSize + maskSize);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);

            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    bw.Write(c.B);
                    bw.Write(c.G);
                    bw.Write(c.R);
                    bw.Write(c.A);
                }
            }

            bw.Write(new byte[maskSize]);
            bw.Flush();
            return ms.ToArray();
        }
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    private static void WriteVariantSheet(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 96, 128 };
        int width = 860;
        int rowHeight = 140;
        int top = 56;
        int height = top + VariantNames.Length * rowHeight + 16;

        using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(245, 245, 245));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var labelFont = new Font("Segoe UI", 13f))
            using (var sizeFont = new Font("Segoe UI", 8.5f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (var sizeBrush = new SolidBrush(Color.FromArgb(130, 130, 130)))
            using (var line = new Pen(Color.FromArgb(225, 225, 225)))
            {
                int x = 300;

                foreach (int size in sizes)
                {
                    var box = new RectangleF(x - 8, 14, size + 16, 16);
                    using (var centered = new StringFormat())
                    {
                        centered.Alignment = StringAlignment.Center;
                        g.DrawString(size.ToString(), sizeFont, sizeBrush, box, centered);
                    }
                    x += size + 20;
                }

                for (int i = 0; i < VariantNames.Length; i++)
                {
                    int rowY = top + i * rowHeight;

                    if (i > 0)
                    {
                        g.DrawLine(line, 20, rowY, width - 20, rowY);
                    }

                    g.DrawString(VariantNames[i], labelFont, labelBrush, 20, rowY + 38);

                    x = 300;
                    foreach (int size in sizes)
                    {
                        using (Bitmap icon = RenderIcon(size, (Variant)i))
                        {
                            float y = rowY + rowHeight / 2f - size / 2f;
                            g.DrawImage(icon, x, y, size, size);
                        }
                        x += size + 20;
                    }
                }
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

    private static void WritePreviewSheet(string path, Variant variant)
    {
        int width = 640;
        int height = 360;

        using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(245, 245, 245));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int x = 40;
            foreach (int size in new[] { 16, 24, 32, 48 })
            {
                using (Bitmap icon = RenderIcon(size, variant))
                {
                    g.DrawImage(icon, x, 40, size, size);
                }
                x += size + 24;
            }

            using (Bitmap big = RenderIcon(256, variant))
            {
                g.DrawImage(big, 40, 90, 256, 256);
            }

            using (var font = new Font("Segoe UI", 10f))
            using (var brush = new SolidBrush(Color.FromArgb(80, 80, 80)))
            {
                g.DrawString("16  24  32  48", font, brush, 40, 16);
                g.DrawString("256", font, brush, 320, 120);
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }

    // ---------- social preview ----------

    private static void WriteSocial(string path, int width, int height, Variant variant)
    {
        using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.FromArgb(18, 20, 24));

            using (var glow = new LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                Color.FromArgb(40, 255, 219, 77),
                Color.FromArgb(0, 255, 219, 77),
                35f))
            {
                g.FillRectangle(glow, new Rectangle(0, 0, width, height));
            }

            using (Bitmap icon = RenderIcon(180, variant))
            {
                g.DrawImage(icon, 96, height / 2 - 90, 180, 180);
            }

            int textX = 330;

            using (var titleFont = new Font("Segoe UI", 58f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var titleBrush = new SolidBrush(Color.FromArgb(245, 246, 248)))
            {
                g.DrawString("Yandex Link Router", titleFont, titleBrush, textX, 190);
            }

            using (var accent = new SolidBrush(Color.FromArgb(255, 219, 77)))
            {
                g.FillRectangle(accent, textX + 4, 268, 120, 6);
            }

            using (var subFont = new Font("Segoe UI", 30f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var subBrush = new SolidBrush(Color.FromArgb(200, 205, 214)))
            {
                g.DrawString("Yandex Music links open in the desktop app.", subFont, subBrush, textX, 310);
                g.DrawString("Everything else goes to your browser.", subFont, subBrush, textX, 352);
            }

            using (var smallFont = new Font("Segoe UI", 22f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var smallBrush = new SolidBrush(Color.FromArgb(140, 147, 158)))
            {
                g.DrawString("Windows 10 / 11   \u00B7   MIT   \u00B7   github.com/Zurrue/yandex-link-router",
                    smallFont, smallBrush, textX, 470);
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }
}
