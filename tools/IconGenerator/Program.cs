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
    // 1. APP ICON: Studio microphone in modern teal/cyan with metallic styling
    // =========================================================================
    private static void DrawAppIcon(DrawingContext dc, int size)
    {
        double s = size;

        // Background: Sleek rounded squircle badge (deep navy/slate + glowing teal border)
        double radius = s * 0.22;
        Rect bgRect = new Rect(s * 0.04, s * 0.04, s * 0.92, s * 0.92);

        var bgBrush = new LinearGradientBrush(
            Color.FromRgb(0x0B, 0x11, 0x1E), // Deep midnight navy
            Color.FromRgb(0x06, 0x22, 0x28), // Deep dark teal shadow
            new Point(0.1, 0.1),
            new Point(0.9, 0.9)
        );

        var borderBrush = new LinearGradientBrush(
            Color.FromArgb(0xE0, 0x2D, 0xD4, 0xBF), // Bright teal top
            Color.FromArgb(0x80, 0x0D, 0x94, 0x88), // Deep teal bottom
            new Point(0.2, 0.0),
            new Point(0.8, 1.0)
        );

        double borderWidth = Math.Max(1.0, s * 0.038);
        var borderPen = new Pen(borderBrush, borderWidth);

        dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, radius, radius);

        // Inner radial glow
        var innerGlow = new RadialGradientBrush(
            Color.FromArgb(0x35, 0x14, 0xB8, 0xA6),
            Color.FromArgb(0x00, 0x0D, 0x94, 0x88)
        )
        {
            Center = new Point(0.5, 0.45),
            GradientOrigin = new Point(0.5, 0.40),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        dc.DrawRoundedRectangle(innerGlow, null, bgRect, radius, radius);

        // Soundwave / Anchor arcs (for medium to large sizes)
        if (s >= 32)
        {
            DrawSoundwaves(dc, s, Color.FromArgb(0x50, 0x38, 0xBD, 0xF8));
        }

        // --- Studio Microphone Elements ---
        double cx = s * 0.5;
        double cy = s * 0.43;

        // Capsule dimensions
        double capW = s * 0.24;
        double capH = s * 0.35;
        double capR = capW * 0.5;
        double capTop = cy - capH * 0.5;

        // Grille (top half of capsule)
        double grilleH = capH * 0.52;
        Rect grilleRect = new Rect(cx - capW * 0.5, capTop, capW, grilleH);

        // Capsule chassis (bottom half)
        Rect bodyRect = new Rect(cx - capW * 0.5, capTop + grilleH * 0.9, capW, capH * 0.52);

        // 1. Grille metallic shading
        var grilleBrush = new LinearGradientBrush(
            Color.FromRgb(0x47, 0x55, 0x69), // Slate
            Color.FromRgb(0x0F, 0x17, 0x2A), // Dark slate
            new Point(0, 0.5),
            new Point(1, 0.5)
        );
        grilleBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE2, 0xE8, 0xF0), 0.35)); // Chrome highlight

        // Draw top rounded capsule part
        var grilleGeometry = new StreamGeometry();
        using (var ctx = grilleGeometry.Open())
        {
            ctx.BeginFigure(new Point(cx - capW * 0.5, capTop + capR), true, true);
            ctx.ArcTo(new Point(cx + capW * 0.5, capTop + capR), new Size(capR, capR), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(cx + capW * 0.5, capTop + grilleH), true, true);
            ctx.LineTo(new Point(cx - capW * 0.5, capTop + grilleH), true, true);
        }
        grilleGeometry.Freeze();
        dc.DrawGeometry(grilleBrush, new Pen(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), Math.Max(0.5, s * 0.015)), grilleGeometry);

        // Grille mesh texture details (for larger sizes)
        if (s >= 48)
        {
            var meshPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Math.Max(0.5, s * 0.01));
            for (double my = capTop + capR * 0.6; my < capTop + grilleH; my += s * 0.035)
            {
                dc.DrawLine(meshPen, new Point(cx - capW * 0.42, my), new Point(cx + capW * 0.42, my));
            }
        }

        // Grille metallic divider ring
        var ringBrush = new LinearGradientBrush(
            Color.FromRgb(0x14, 0xB8, 0xA6), // Teal
            Color.FromRgb(0x2D, 0xD4, 0xBF), // Cyan highlight
            new Point(0, 0),
            new Point(1, 0)
        );
        dc.DrawRectangle(ringBrush, null, new Rect(cx - capW * 0.52, capTop + grilleH - s * 0.018, capW * 1.04, Math.Max(1.5, s * 0.032)));

        // 2. Microphone lower body (Teal metallic cylinder)
        var bodyBrush = new LinearGradientBrush(
            Color.FromRgb(0x04, 0x2F, 0x2E), // Deep emerald
            Color.FromRgb(0x0F, 0x76, 0x6E), // Teal body
            new Point(0, 0.5),
            new Point(1, 0.5)
        );
        bodyBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0xD4, 0xBF), 0.38)); // Lustrous specular line

        var bodyGeometry = new StreamGeometry();
        using (var ctx = bodyGeometry.Open())
        {
            ctx.BeginFigure(new Point(cx - capW * 0.5, capTop + grilleH), true, true);
            ctx.LineTo(new Point(cx + capW * 0.5, capTop + grilleH), true, true);
            ctx.LineTo(new Point(cx + capW * 0.5, capTop + capH - capR), true, true);
            ctx.ArcTo(new Point(cx - capW * 0.5, capTop + capH - capR), new Size(capR, capR), 0, false, SweepDirection.Clockwise, true, true);
        }
        bodyGeometry.Freeze();
        dc.DrawGeometry(bodyBrush, new Pen(new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88)), Math.Max(0.5, s * 0.015)), bodyGeometry);

        // Body Center Status Dot / Anchor emblem
        if (s >= 32)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)), null, new Point(cx, capTop + capH * 0.76), Math.Max(1.0, s * 0.022), Math.Max(1.0, s * 0.022));
        }

        // 3. Shock-Mount / U-Cradle
        double cradleW = capW * 1.55;
        double cradleH = capH * 0.65;
        double cradleTop = cy - capH * 0.05;
        double cradleBottom = cy + capH * 0.58;
        double cradleStroke = Math.Max(1.5, s * 0.042);

        var chromeBrush = new LinearGradientBrush(
            Color.FromRgb(0x94, 0xA3, 0xB8),
            Color.FromRgb(0xF8, 0xFA, 0xFC),
            new Point(0, 0),
            new Point(1, 1)
        );
        var cradlePen = new Pen(chromeBrush, cradleStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var cradlePath = new StreamGeometry();
        using (var ctx = cradlePath.Open())
        {
            ctx.BeginFigure(new Point(cx - cradleW * 0.5, cradleTop), false, false);
            ctx.ArcTo(new Point(cx + cradleW * 0.5, cradleTop), new Size(cradleW * 0.5, cradleH), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        cradlePath.Freeze();
        dc.DrawGeometry(null, cradlePen, cradlePath);

        // Side thumbscrew knobs on cradle
        double knobR = Math.Max(1.0, s * 0.028);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)), null, new Point(cx - cradleW * 0.5, cradleTop), knobR, knobR);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)), null, new Point(cx + cradleW * 0.5, cradleTop), knobR, knobR);

        // 4. Vertical Stand Post
        double postTop = cradleBottom;
        double postBottom = s * 0.77;
        double postW = Math.Max(1.8, s * 0.045);
        dc.DrawLine(new Pen(chromeBrush, postW), new Point(cx, postTop), new Point(cx, postBottom));

        // 5. Bevelled Weighted Disc Base
        double baseW = s * 0.48;
        double baseH = Math.Max(2.5, s * 0.08);
        var baseBrush = new LinearGradientBrush(
            Color.FromRgb(0x33, 0x41, 0x55),
            Color.FromRgb(0x0F, 0x17, 0x2A),
            new Point(0.5, 0),
            new Point(0.5, 1)
        );
        var basePen = new Pen(new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)), Math.Max(0.8, s * 0.02));
        dc.DrawEllipse(baseBrush, basePen, new Point(cx, postBottom), baseW * 0.5, baseH * 0.5);
    }

    private static void DrawSoundwaves(DrawingContext dc, double s, Color waveColor)
    {
        double cx = s * 0.5;
        double cy = s * 0.38;
        var wavePen = new Pen(new SolidColorBrush(waveColor), Math.Max(1.0, s * 0.025))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        // Left arc
        double r1 = s * 0.28;
        var leftArc = new StreamGeometry();
        using (var ctx = leftArc.Open())
        {
            ctx.BeginFigure(new Point(cx - r1 * 0.75, cy - r1 * 0.6), false, false);
            ctx.ArcTo(new Point(cx - r1 * 0.75, cy + r1 * 0.6), new Size(r1, r1), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        leftArc.Freeze();
        dc.DrawGeometry(null, wavePen, leftArc);

        // Right arc
        var rightArc = new StreamGeometry();
        using (var ctx = rightArc.Open())
        {
            ctx.BeginFigure(new Point(cx + r1 * 0.75, cy - r1 * 0.6), false, false);
            ctx.ArcTo(new Point(cx + r1 * 0.75, cy + r1 * 0.6), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, true);
        }
        rightArc.Freeze();
        dc.DrawGeometry(null, wavePen, rightArc);
    }

    // =========================================================================
    // 2. ACTIVE / LOCKED ICON: Crisp active microphone with cyan/teal glow
    // =========================================================================
    private static void DrawActiveIcon(DrawingContext dc, int size)
    {
        double s = size;

        // Circular high-contrast disc: stands out on BOTH black & white taskbars
        double cx = s * 0.5;
        double cy = s * 0.5;
        double discR = s * 0.45;

        var discBrush = new LinearGradientBrush(
            Color.FromRgb(0x06, 0x11, 0x1E),
            Color.FromRgb(0x04, 0x22, 0x26),
            new Point(0.2, 0),
            new Point(0.8, 1)
        );
        var discBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6)), Math.Max(1.0, s * 0.05));
        dc.DrawEllipse(discBrush, discBorder, new Point(cx, cy), discR, discR);

        // Inner glowing teal aura
        var aura = new RadialGradientBrush(
            Color.FromArgb(0x45, 0x06, 0xB6, 0xD4),
            Color.FromArgb(0x00, 0x0D, 0x94, 0x88)
        )
        {
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        dc.DrawEllipse(aura, null, new Point(cx, cy), discR, discR);

        // Mic Dimensions
        double micCenterY = s * 0.44;
        double capW = s * 0.26;
        double capH = s * 0.38;
        double capR = capW * 0.5;
        double capTop = micCenterY - capH * 0.5;

        // Glowing Cyan Microphone Capsule
        var micBrush = new LinearGradientBrush(
            Color.FromRgb(0x00, 0xF2, 0xFE), // Vivid electric cyan
            Color.FromRgb(0x14, 0xB8, 0xA6), // Modern teal
            new Point(0.1, 0.1),
            new Point(0.9, 0.9)
        );
        micBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0xF2, 0xFE), 0.35)); // Bright reflection

        // Capsule Body
        Rect capRect = new Rect(cx - capW * 0.5, capTop, capW, capH);
        dc.DrawRoundedRectangle(micBrush, null, capRect, capR, capR);

        // Grille divider line
        if (s >= 24)
        {
            double divY = capTop + capH * 0.48;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x04, 0x2F, 0x2E)), Math.Max(0.8, s * 0.025)),
                new Point(cx - capW * 0.48, divY), new Point(cx + capW * 0.48, divY));
        }

        // U-Cradle
        double cradleW = capW * 1.58;
        double cradleH = capH * 0.62;
        double cradleTop = micCenterY - capH * 0.05;
        double cradleBottom = micCenterY + capH * 0.58;
        double cradleStroke = Math.Max(1.4, s * 0.055);

        var cradlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)), cradleStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var cradlePath = new StreamGeometry();
        using (var ctx = cradlePath.Open())
        {
            ctx.BeginFigure(new Point(cx - cradleW * 0.5, cradleTop), false, false);
            ctx.ArcTo(new Point(cx + cradleW * 0.5, cradleTop), new Size(cradleW * 0.5, cradleH), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        cradlePath.Freeze();
        dc.DrawGeometry(null, cradlePen, cradlePath);

        // Vertical stem
        double postBottom = s * 0.78;
        dc.DrawLine(cradlePen, new Point(cx, cradleBottom), new Point(cx, postBottom));

        // Horizontal Base
        double baseW = s * 0.42;
        var basePen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), cradleStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(basePen, new Point(cx - baseW * 0.5, postBottom), new Point(cx + baseW * 0.5, postBottom));

        // Active / Locked Indicator Accent: Tiny glowing green lock/dot at lower right
        if (s >= 24)
        {
            double lockX = cx + s * 0.22;
            double lockY = cy + s * 0.22;
            double dotR = Math.Max(1.5, s * 0.075);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)), new Pen(new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B)), 0.8), new Point(lockX, lockY), dotR, dotR);
        }
    }

    // =========================================================================
    // 3. MUTED / UNLOCKED ICON: Crisp muted mic in slate with clean red slash
    // =========================================================================
    private static void DrawMutedIcon(DrawingContext dc, int size)
    {
        double s = size;

        // Circular high-contrast disc
        double cx = s * 0.5;
        double cy = s * 0.5;
        double discR = s * 0.45;

        var discBrush = new LinearGradientBrush(
            Color.FromRgb(0x0F, 0x17, 0x2A),
            Color.FromRgb(0x1E, 0x29, 0x3B),
            new Point(0.2, 0),
            new Point(0.8, 1)
        );
        var discBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)), Math.Max(1.0, s * 0.05));
        dc.DrawEllipse(discBrush, discBorder, new Point(cx, cy), discR, discR);

        // Mic Dimensions
        double micCenterY = s * 0.44;
        double capW = s * 0.26;
        double capH = s * 0.38;
        double capR = capW * 0.5;
        double capTop = micCenterY - capH * 0.5;

        // Muted Slate Microphone Capsule
        var micBrush = new LinearGradientBrush(
            Color.FromRgb(0x64, 0x74, 0x8B),
            Color.FromRgb(0x47, 0x55, 0x69),
            new Point(0.1, 0.1),
            new Point(0.9, 0.9)
        );

        Rect capRect = new Rect(cx - capW * 0.5, capTop, capW, capH);
        dc.DrawRoundedRectangle(micBrush, null, capRect, capR, capR);

        // U-Cradle
        double cradleW = capW * 1.58;
        double cradleH = capH * 0.62;
        double cradleTop = micCenterY - capH * 0.05;
        double cradleBottom = micCenterY + capH * 0.58;
        double cradleStroke = Math.Max(1.4, s * 0.055);

        var cradlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), cradleStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var cradlePath = new StreamGeometry();
        using (var ctx = cradlePath.Open())
        {
            ctx.BeginFigure(new Point(cx - cradleW * 0.5, cradleTop), false, false);
            ctx.ArcTo(new Point(cx + cradleW * 0.5, cradleTop), new Size(cradleW * 0.5, cradleH), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        cradlePath.Freeze();
        dc.DrawGeometry(null, cradlePen, cradlePath);

        // Stem & Base
        double postBottom = s * 0.78;
        dc.DrawLine(cradlePen, new Point(cx, cradleBottom), new Point(cx, postBottom));
        double baseW = s * 0.42;
        dc.DrawLine(cradlePen, new Point(cx - baseW * 0.5, postBottom), new Point(cx + baseW * 0.5, postBottom));

        // Bold Red Diagonal Slash (Top-Left to Bottom-Right)
        double slashP1 = s * 0.22;
        double slashP2 = s * 0.78;
        Point pStart = new Point(slashP1, slashP1);
        Point pEnd = new Point(slashP2, slashP2);

        // Dark Outline around slash for razor-sharp visual isolation
        double slashStroke = Math.Max(2.2, s * 0.09);
        double outlineStroke = slashStroke + Math.Max(1.5, s * 0.05);

        var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), outlineStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(outlinePen, pStart, pEnd);

        // Vivid Crimson Red Slash
        var slashBrush = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0x4D, 0x4D), // Bright red
            Color.FromRgb(0xDC, 0x26, 0x26), // Deep crimson
            new Point(0, 0),
            new Point(1, 1)
        );
        var slashPen = new Pen(slashBrush, slashStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(slashPen, pStart, pEnd);
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
