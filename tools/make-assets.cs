using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Generates assets/icon.ico and assets/social-preview.png without external tools.
internal static class MakeAssets
{
    private static readonly int[] IconSizes = { 16, 24, 32, 48, 256 };

    private static void Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);

        string ico = Path.Combine(assets, "icon.ico");
        WriteIcon(ico, IconSizes);

        string sheet = Path.Combine(assets, "icon-preview.png");
        WritePreviewSheet(sheet);

        string social = Path.Combine(assets, "social-preview.png");
        WriteSocial(social, 1280, 640);

        Console.WriteLine("icon.ico            " + new FileInfo(ico).Length + " bytes");
        Console.WriteLine("icon-preview.png    " + new FileInfo(sheet).Length + " bytes");
        Console.WriteLine("social-preview.png  " + new FileInfo(social).Length + " bytes");
    }

    // ---------- icon ----------

    private sealed class Frame
    {
        public int Size;
        public byte[] Data;
    }

    private static void WriteIcon(string path, int[] sizes)
    {
        var frames = new Frame[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            using (Bitmap bmp = RenderIcon(size))
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

            foreach (var frame in frames)
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

            foreach (var frame in frames)
            {
                w.Write(frame.Data);
            }
        }
    }

    private static Bitmap RenderIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float pad = Math.Max(0.5f, size * 0.02f);
            var rect = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);

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

            // eighth note: head + stem + flag, drawn as shapes so it stays crisp at 16 px
            bool small = size <= 24;
            float cx = size * 0.40f;
            float cy = size * 0.68f;
            float headW = size * (small ? 0.42f : 0.34f);
            float headH = size * (small ? 0.32f : 0.25f);

            using (var brush = new SolidBrush(Color.FromArgb(26, 26, 26)))
            {
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-22f);
                g.FillEllipse(brush, -headW / 2, -headH / 2, headW, headH);
                g.Restore(state);

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
        }

        return bmp;
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

    private static void WritePreviewSheet(string path)
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
                using (Bitmap icon = RenderIcon(size))
                {
                    g.DrawImage(icon, x, 40, size, size);
                }
                x += size + 24;
            }

            using (Bitmap big = RenderIcon(256))
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

    private static void WriteSocial(string path, int width, int height)
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

            using (Bitmap icon = RenderIcon(180))
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
                g.DrawString("Windows 10 / 11   ·   MIT   ·   github.com/Zurrue/yandex-link-router",
                    smallFont, smallBrush, textX, 470);
            }

            bmp.Save(path, ImageFormat.Png);
        }
    }
}
