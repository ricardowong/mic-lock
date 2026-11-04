using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MicLock.IconGen;

public enum IconKind
{
    App,
    Active,
    Muted
}

public static class Program
{
    private static readonly int[] StandardSizes = [16, 24, 32, 48, 64, 256];

    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("=== MicAnchor Multi-Resolution Icon Generator ===");

        string solutionDir = FindSolutionDir();
        Console.WriteLine($"Solution Directory: {solutionDir}");

        string assetsDir = Path.Combine(solutionDir, "src", "MicLock.App", "Assets");
        Directory.CreateDirectory(assetsDir);

        string artifactDir = @"C:\Users\ricar\.gemini\antigravity\brain\96ef5cf8-526e-4947-b631-d306addf4c0e";
        if (Directory.Exists(artifactDir))
        {
            Directory.CreateDirectory(artifactDir);
        }

        // 1. Generate App Icon (Clean studio mic in modern teal/cyan with metallic styling)
        Console.WriteLine("\n[1/3] Generating app.ico...");
        var appFrames = GenerateFrames(IconKind.App);
        string appIcoPath = Path.Combine(assetsDir, "app.ico");
        SaveIco(appIcoPath, appFrames);
        SavePreviewPngs(appFrames, IconKind.App, assetsDir, artifactDir);

        // 2. Generate Active/Locked Icon (Crisp active mic with cyan/teal glow)
        Console.WriteLine("\n[2/3] Generating mic-locked.ico and mic-active.ico...");
        var activeFrames = GenerateFrames(IconKind.Active);
        string lockedIcoPath = Path.Combine(assetsDir, "mic-locked.ico");
        string activeIcoPath = Path.Combine(assetsDir, "mic-active.ico");
        SaveIco(lockedIcoPath, activeFrames);
        SaveIco(activeIcoPath, activeFrames);
        SavePreviewPngs(activeFrames, IconKind.Active, assetsDir, artifactDir);

        // 3. Generate Muted/Unlocked Icon (Crisp muted mic in slate with red slash)
        Console.WriteLine("\n[3/3] Generating mic-unlocked.ico and mic-muted.ico...");
        var mutedFrames = GenerateFrames(IconKind.Muted);
        string unlockedIcoPath = Path.Combine(assetsDir, "mic-unlocked.ico");
        string mutedIcoPath = Path.Combine(assetsDir, "mic-muted.ico");
        SaveIco(unlockedIcoPath, mutedFrames);
        SaveIco(mutedIcoPath, mutedFrames);
        SavePreviewPngs(mutedFrames, IconKind.Muted, assetsDir, artifactDir);

        Console.WriteLine("\n=== Summary of Generated Icons ===");
        PrintFileInfo(appIcoPath);
        PrintFileInfo(lockedIcoPath);
        PrintFileInfo(activeIcoPath);
        PrintFileInfo(unlockedIcoPath);
        PrintFileInfo(mutedIcoPath);

        Console.WriteLine("\nAll icons generated and verified successfully!");
    }

    private static void PrintFileInfo(string path)
    {
        var fi = new FileInfo(path);
        Console.WriteLine($"  {fi.Name,-18} : {fi.Length,6} bytes ({fi.Length / 1024.0:F1} KB) -> {fi.FullName}");
    }

    private static string FindSolutionDir()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MicLock.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }
        return @"C:\Users\ricar\Downloads\mic-lock";
    }

    private static List<(int Size, byte[] PngData)> GenerateFrames(IconKind kind)
    {
        var frames = new List<(int Size, byte[] PngData)>();
        foreach (int size in StandardSizes)
        {
            byte[] png = RenderIconToPng(kind, size);
            frames.Add((size, png));
            Console.WriteLine($"   - {size}x{size} frame rendered: {png.Length} bytes");
        }
        return frames;
    }

    private static void SavePreviewPngs(List<(int Size, byte[] PngData)> frames, IconKind kind, string assetsDir, string artifactDir)
    {
        string prefix = kind switch
        {
            IconKind.App => "app",
            IconKind.Active => "mic-active",
            IconKind.Muted => "mic-muted",
            _ => "icon"
        };

        foreach (var (size, png) in frames)
        {
            if (size == 16 || size == 32 || size == 256)
            {
                string fileName = $"{prefix}-{size}.png";
                if (Directory.Exists(artifactDir))
                {
                    File.WriteAllBytes(Path.Combine(artifactDir, fileName), png);
                }
            }
        }
    }

    private static byte[] RenderIconToPng(IconKind kind, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            switch (kind)
            {
                case IconKind.App:
                    DrawAppIcon(dc, size);
                    break;
                case IconKind.Active:
                    DrawActiveIcon(dc, size);
                    break;
                case IconKind.Muted:
                    DrawMutedIcon(dc, size);
                    break;
            }
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // =========================================================================
    // 1. APP ICON: Apple SF-Symbols style minimalist microphone in macOS squircle
    // =========================================================================
    private static void DrawAppIcon(DrawingContext dc, int size)
    {
        double s = size;

        // macOS Squircle Badge (Sky to Deep Cobalt)
        double radius = s * 0.225;
        Rect bgRect = new Rect(s * 0.04, s * 0.04, s * 0.92, s * 0.92);

        var bgBrush = new LinearGradientBrush(
            Color.FromRgb(0x02, 0x84, 0xC7), // Apple/Sky Blue (#0284C7)
            Color.FromRgb(0x03, 0x69, 0xA1), // Deep Cobalt (#0369A1)
            new Point(0.1, 0.0),
            new Point(0.9, 1.0)
        );

        var borderBrush = new LinearGradientBrush(
            Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF),
            new Point(0.0, 0.0),
            new Point(0.0, 1.0)
        );

        double borderWidth = Math.Max(1.0, s * 0.03);
        var borderPen = new Pen(borderBrush, borderWidth);

        dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, radius, radius);

        // Vector Glyph (White SF-style Mic)
        DrawMinimalistMicGlyph(dc, s, Color.FromRgb(0xFF, 0xFF, 0xFF), hasLockBadge: false, isMuted: false);
    }

    // =========================================================================
    // 2. ACTIVE / LOCKED TRAY ICON: Crisp transparent SF-style vector glyph
    // =========================================================================
    private static void DrawActiveIcon(DrawingContext dc, int size)
    {
        double s = size;
        // Vibrant sky cyan (#38BDF8) with subtle dark contrast halo for light taskbars
        DrawMinimalistMicGlyph(dc, s, Color.FromRgb(0x38, 0xBD, 0xF8), hasLockBadge: true, isMuted: false);
    }

    // =========================================================================
    // 3. MUTED TRAY ICON: Crisp coral red with 45-degree optical slash
    // =========================================================================
    private static void DrawMutedIcon(DrawingContext dc, int size)
    {
        double s = size;
        // Vivid crimson (#EF4444)
        DrawMinimalistMicGlyph(dc, s, Color.FromRgb(0xEF, 0x44, 0x44), hasLockBadge: false, isMuted: true);
    }

    private static void DrawMinimalistMicGlyph(DrawingContext dc, double s, Color strokeColor, bool hasLockBadge, bool isMuted)
    {
        double cx = s * 0.50;
        double cy = s * 0.42;

        // Proportions derived from Apple SF Symbols `mic` and `mic.slash`
        double capW = Math.Round(s * 0.28);
        if (((int)capW) % 2 != 0) capW += 1.0; // Ensure even for symmetry
        double capH = Math.Round(s * 0.44);
        double capR = capW * 0.5;
        double capTop = cy - capH * 0.52;

        double strokeW = Math.Max(1.4, Math.Round(s * 0.082));
        var strokeBrush = new SolidColorBrush(strokeColor);
        var glyphPen = new Pen(strokeBrush, strokeW)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        // Dark back-shadow pen for dual light/dark taskbar contrast
        var haloPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x00, 0x00)), strokeW + Math.Max(1.0, s * 0.04))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        // 1. Microphone Capsule
        Rect capRect = new Rect(cx - capW * 0.5, capTop, capW, capH);
        var fillBrush = new SolidColorBrush(Color.FromArgb(0x35, strokeColor.R, strokeColor.G, strokeColor.B));

        // Draw halo first
        dc.DrawRoundedRectangle(null, haloPen, capRect, capR, capR);
        dc.DrawRoundedRectangle(fillBrush, glyphPen, capRect, capR, capR);

        // 2. Cradle Arc
        double cradleW = Math.Round(capW * 1.62);
        double cradleTop = capTop + capH * 0.38;
        double cradleBottom = capTop + capH + s * 0.06;

        var cradlePath = new StreamGeometry();
        using (var ctx = cradlePath.Open())
        {
            ctx.BeginFigure(new Point(cx - cradleW * 0.5, cradleTop), false, false);
            ctx.ArcTo(new Point(cx + cradleW * 0.5, cradleTop), new Size(cradleW * 0.5, cradleBottom - cradleTop), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        cradlePath.Freeze();

        dc.DrawGeometry(null, haloPen, cradlePath);
        dc.DrawGeometry(null, glyphPen, cradlePath);

        // 3. Vertical Stem
        double postBottom = s * 0.82;
        dc.DrawLine(haloPen, new Point(cx, cradleBottom), new Point(cx, postBottom));
        dc.DrawLine(glyphPen, new Point(cx, cradleBottom), new Point(cx, postBottom));

        // 4. Base Foot
        double baseW = Math.Round(s * 0.40);
        dc.DrawLine(haloPen, new Point(cx - baseW * 0.5, postBottom), new Point(cx + baseW * 0.5, postBottom));
        dc.DrawLine(glyphPen, new Point(cx - baseW * 0.5, postBottom), new Point(cx + baseW * 0.5, postBottom));

        // 5. Lock Badge (if active/locked)
        if (hasLockBadge && s >= 32)
        {
            double badgeR = s * 0.16;
            double bx = s * 0.78;
            double by = s * 0.78;
            var badgeBg = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
            var badgeBorder = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), Math.Max(1.0, s * 0.035));
            dc.DrawEllipse(badgeBg, badgeBorder, new Point(bx, by), badgeR, badgeR);

            // Shackle + body
            double lockW = badgeR * 1.1;
            double lockH = badgeR * 0.9;
            Rect lockRect = new Rect(bx - lockW * 0.5, by - lockH * 0.1, lockW, lockH);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), null, lockRect, 1.5, 1.5);

            double shackleW = lockW * 0.65;
            double shackleH = badgeR * 0.75;
            var shacklePen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), Math.Max(1.0, s * 0.035))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var shacklePath = new StreamGeometry();
            using (var ctx = shacklePath.Open())
            {
                ctx.BeginFigure(new Point(bx - shackleW * 0.5, by - lockH * 0.1), false, false);
                ctx.ArcTo(new Point(bx + shackleW * 0.5, by - lockH * 0.1), new Size(shackleW * 0.5, shackleH), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            shacklePath.Freeze();
            dc.DrawGeometry(null, shacklePen, shacklePath);
        }

        // 6. Diagonal Slash with Optical Cutout Masking (if muted)
        if (isMuted)
        {
            double p1 = s * 0.16;
            double p2 = s * 0.84;
            Point start = new Point(p1, p1);
            Point end = new Point(p2, p2);

            // Cutout halo (dark background mask)
            double slashStroke = strokeW * 1.35;
            var slashMaskPen = new Pen(new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x17, 0x2A)), slashStroke + strokeW * 1.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(slashMaskPen, start, end);

            // Crimson red slash line
            var slashPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), slashStroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(slashPen, start, end);
        }
    }

    // =========================================================================
    // ICO BINARY SERIALIZER: Multi-resolution with PNG frames
    // =========================================================================
    public static void SaveIco(string filePath, List<(int Size, byte[] PngData)> frames)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // 1. ICONDIR header (6 bytes)
        bw.Write((ushort)0); // idReserved
        bw.Write((ushort)1); // idType = 1 (ICO)
        bw.Write((ushort)frames.Count); // idCount

        // 2. ICONDIRENTRY (16 bytes per image)
        int offset = 6 + (16 * frames.Count);

        foreach (var (size, png) in frames)
        {
            byte w = size >= 256 ? (byte)0 : (byte)size;
            byte h = size >= 256 ? (byte)0 : (byte)size;
            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0); // bColorCount
            bw.Write((byte)0); // bReserved
            bw.Write((ushort)1); // wPlanes
            bw.Write((ushort)32); // wBitCount
            bw.Write((uint)png.Length); // dwBytesInRes
            bw.Write((uint)offset); // dwImageOffset

            offset += png.Length;
        }

        // 3. PNG image bytes
        foreach (var (_, png) in frames)
        {
            bw.Write(png);
        }
    }
}
