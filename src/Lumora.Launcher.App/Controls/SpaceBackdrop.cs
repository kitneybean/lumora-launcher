using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Lumora.Launcher.App.Controls;

/// <summary>
/// Procedural pixel-art space scene: dithered sky, nebula, stars, a blocky planet
/// with oceans, biomes, clouds and village lights, plus cube-shaped moon and asteroids.
/// Rendered into a low-resolution bitmap and scaled up without smoothing.
/// When animated, a few stars twinkle and a shooting star crosses the sky now and then.
/// </summary>
public sealed class SpaceBackdrop : Control
{
    public static readonly StyledProperty<int> SeedProperty =
        AvaloniaProperty.Register<SpaceBackdrop, int>(nameof(Seed), 7);

    public static readonly StyledProperty<double> PixelScaleProperty =
        AvaloniaProperty.Register<SpaceBackdrop, double>(nameof(PixelScale), 4);

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<SpaceBackdrop, bool>(nameof(IsAnimated), true);

    /// <summary>Planet center, relative to the control size.</summary>
    public static readonly StyledProperty<Point> PlanetCenterProperty =
        AvaloniaProperty.Register<SpaceBackdrop, Point>(nameof(PlanetCenter), new Point(0.86, 0.86));

    /// <summary>Planet radius relative to the smaller side.</summary>
    public static readonly StyledProperty<double> PlanetSizeProperty =
        AvaloniaProperty.Register<SpaceBackdrop, double>(nameof(PlanetSize), 0.42);

    /// <summary>Moon (cube) center, relative to the control size.</summary>
    public static readonly StyledProperty<Point> MoonCenterProperty =
        AvaloniaProperty.Register<SpaceBackdrop, Point>(nameof(MoonCenter), new Point(0.62, 0.42));

    static SpaceBackdrop()
    {
        AffectsRender<SpaceBackdrop>(SeedProperty, PixelScaleProperty, PlanetCenterProperty, PlanetSizeProperty, MoonCenterProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SpaceBackdrop>(true);
        IsHitTestVisibleProperty.OverrideDefaultValue<SpaceBackdrop>(false);
    }

    public int Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
    public double PixelScale { get => GetValue(PixelScaleProperty); set => SetValue(PixelScaleProperty, value); }
    public bool IsAnimated { get => GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }
    public Point PlanetCenter { get => GetValue(PlanetCenterProperty); set => SetValue(PlanetCenterProperty, value); }
    public double PlanetSize { get => GetValue(PlanetSizeProperty); set => SetValue(PlanetSizeProperty, value); }
    public Point MoonCenter { get => GetValue(MoonCenterProperty); set => SetValue(MoonCenterProperty, value); }

    private WriteableBitmap? _bitmap;
    private (int W, int H, int Seed, Point Planet, double Size, Point Moon) _key;
    private readonly List<Twinkle> _twinkles = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly DateTime _start = DateTime.UtcNow;
    private Random _random = new(7);
    private ShootingStar? _shooting;
    private double _nextShootingAt = 4;

    private readonly record struct Twinkle(int X, int Y, uint Color, double Speed, double Phase);
    private sealed record ShootingStar(double X, double Y, double Dx, double Dy, double StartedAt, double Duration);

    public SpaceBackdrop()
    {
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsAnimatedProperty || change.Property == IsVisibleProperty)
            UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (IsAnimated && IsVisible && VisualRoot is not null)
            _timer.Start();
        else
            _timer.Stop();
        InvalidateVisual();
    }

    private double Time => (DateTime.UtcNow - _start).TotalSeconds;

    public override void Render(DrawingContext context)
    {
        var scale = Math.Max(1, PixelScale);
        var w = (int)Math.Ceiling(Bounds.Width / scale);
        var h = (int)Math.Ceiling(Bounds.Height / scale);
        if (w < 2 || h < 2)
            return;

        var key = (w, h, Seed, PlanetCenter, PlanetSize, MoonCenter);
        if (_bitmap is null || key != _key)
        {
            _bitmap?.Dispose();
            _bitmap = Generate(w, h);
            _key = key;
        }

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(_bitmap, new Rect(0, 0, w, h), new Rect(0, 0, w * scale, h * scale));

        if (!IsAnimated)
            return;

        var t = Time;
        foreach (var tw in _twinkles)
        {
            var a = 0.5 + 0.5 * Math.Sin(t * tw.Speed + tw.Phase);
            a = a * a;
            var core = new SolidColorBrush(Color.FromUInt32(tw.Color), a);
            var arm = new SolidColorBrush(Color.FromUInt32(tw.Color), a * 0.45);
            context.FillRectangle(core, new Rect(tw.X * scale, tw.Y * scale, scale, scale));
            context.FillRectangle(arm, new Rect((tw.X - 1) * scale, tw.Y * scale, scale, scale));
            context.FillRectangle(arm, new Rect((tw.X + 1) * scale, tw.Y * scale, scale, scale));
            context.FillRectangle(arm, new Rect(tw.X * scale, (tw.Y - 1) * scale, scale, scale));
            context.FillRectangle(arm, new Rect(tw.X * scale, (tw.Y + 1) * scale, scale, scale));
        }

        DrawShootingStar(context, t, w, h, scale);
    }

    private void DrawShootingStar(DrawingContext context, double t, int w, int h, double scale)
    {
        if (_shooting is null && t >= _nextShootingAt)
        {
            var x = w * (0.15 + _random.NextDouble() * 0.6);
            var y = h * (0.02 + _random.NextDouble() * 0.25);
            var speed = 70 + _random.NextDouble() * 50;
            _shooting = new ShootingStar(x, y, -speed, speed * 0.45, t, 0.9);
            _timer.Interval = TimeSpan.FromMilliseconds(33);
        }
        if (_shooting is not { } s)
            return;

        var p = (t - s.StartedAt) / s.Duration;
        if (p >= 1)
        {
            _shooting = null;
            _nextShootingAt = t + 7 + _random.NextDouble() * 10;
            _timer.Interval = TimeSpan.FromMilliseconds(125);
            return;
        }

        var fade = Math.Sin(p * Math.PI);
        var len = Math.Sqrt(s.Dx * s.Dx + s.Dy * s.Dy);
        var ux = s.Dx / len;
        var uy = s.Dy / len;
        var hx = s.X + s.Dx * (t - s.StartedAt);
        var hy = s.Y + s.Dy * (t - s.StartedAt);
        const int tail = 14;
        for (var i = 0; i < tail; i++)
        {
            var px = Math.Round(hx - ux * i);
            var py = Math.Round(hy - uy * i);
            var a = fade * (1 - i / (double)tail);
            var color = i == 0 ? Color.FromRgb(255, 255, 255) : Color.FromRgb(170, 235, 255);
            context.FillRectangle(new SolidColorBrush(color, a), new Rect(px * scale, py * scale, scale, scale));
        }
    }

    // ------------------------------------------------------------------
    // Scene generation
    // ------------------------------------------------------------------

    private static readonly uint[] Sky =
    [
        0x05060D, 0x070914, 0x090C1B, 0x0C0F22, 0x100F28, 0x14102D, 0x181131, 0x1C1234,
    ];

    private static readonly int[] Bayer =
    [
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    ];

    private static double BayerAt(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0;

    private unsafe WriteableBitmap Generate(int w, int h)
    {
        var seed = Seed;
        _random = new Random(seed);
        _twinkles.Clear();
        var px = new uint[w * h];

        PaintSky(px, w, h);
        PaintNebula(px, w, h, seed);
        PaintStars(px, w, h, seed);

        var min = Math.Min(w, h);
        // Distant cube asteroids.
        for (var i = 0; i < 3; i++)
        {
            var ax = (int)(w * (0.08 + Hash01(seed, i, 91) * 0.5));
            var ay = (int)(h * (0.15 + Hash01(seed, i, 92) * 0.7));
            var size = 2 + (int)(Hash01(seed, i, 93) * 2);
            DrawCube(px, w, h, ax, ay, size, 0x4A4A58, seed + i, dim: 0.55);
        }

        var radius = PlanetSize * min;
        PaintPlanet(px, w, h, PlanetCenter.X * w, PlanetCenter.Y * h, radius, seed);
        DrawCube(px, w, h, (int)(MoonCenter.X * w), (int)(MoonCenter.Y * h), Math.Max(5, (int)(min * 0.07)), 0x9A9AA6, seed, dim: 1);

        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            for (var y = 0; y < h; y++)
            {
                var row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
                for (var x = 0; x < w; x++)
                    row[x] = 0xFF000000 | px[y * w + x];
            }
        }
        return bitmap;
    }

    private static void PaintSky(uint[] px, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            var v = y / (double)(h - 1) * (Sky.Length - 1);
            var i = (int)v;
            var f = v - i;
            for (var x = 0; x < w; x++)
                px[y * w + x] = Sky[Math.Min(Sky.Length - 1, f > BayerAt(x, y) ? i + 1 : i)];
        }
    }

    private static void PaintNebula(uint[] px, int w, int h, int seed)
    {
        // Diagonal band from the top right towards the bottom left.
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var u = x / (double)w;
                var v = y / (double)h;
                var d = u * 0.8 + v - 1.05;
                var band = Math.Exp(-d * d / 0.09);
                var n = Fbm(x / 34.0, y / 34.0, seed);
                var intensity = Math.Clamp((n - 0.42) * 2.4, 0, 1) * band;
                // Quantize to a few dithered levels for a pixel-art look.
                var level = Math.Floor(intensity * 4 + BayerAt(x, y) - 0.5) / 4.0;
                if (level <= 0)
                    continue;

                var teal = Fbm(x / 22.0 + 40, y / 22.0 + 17, seed + 3);
                var color = teal > 0.62 ? 0x2A8C8Cu : level > 0.5 ? 0x6A3FB8u : 0x3E2878u;
                px[y * w + x] = Blend(px[y * w + x], color, Math.Min(0.7, level * 0.75));
            }
        }
    }

    private void PaintStars(uint[] px, int w, int h, int seed)
    {
        uint[] tints = [0xFFFFFF, 0xCFE3FF, 0xFFF1C9, 0xBFF5DF];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var r = Hash01(seed, x, y);
                if (r > 0.012)
                    continue;
                var tint = tints[(int)(Hash01(seed + 1, x, y) * tints.Length) % tints.Length];
                var brightness = 0.3 + Hash01(seed + 2, x, y) * 0.7;
                var i = y * w + x;
                px[i] = Blend(px[i], tint, brightness);

                if (r < 0.0014 && x > 1 && y > 1 && x < w - 2 && y < h - 2)
                {
                    if (_twinkles.Count < 28)
                        _twinkles.Add(new Twinkle(x, y, 0xFF000000 | tint, 0.8 + Hash01(seed + 4, x, y) * 2.2, Hash01(seed + 5, x, y) * 6.28));
                    else
                    {
                        px[i] = tint;
                        px[i - 1] = Blend(px[i - 1], tint, 0.35);
                        px[i + 1] = Blend(px[i + 1], tint, 0.35);
                        px[i - w] = Blend(px[i - w], tint, 0.35);
                        px[i + w] = Blend(px[i + w], tint, 0.35);
                    }
                }
            }
        }
    }

    private static void PaintPlanet(uint[] px, int w, int h, double cx, double cy, double radius, int seed)
    {
        // Light from the upper left, slightly in front.
        const double lx = -0.62, ly = -0.48, lz = 0.62;
        var atmosphere = Math.Max(2.5, radius * 0.05);

        var x0 = Math.Max(0, (int)(cx - radius - atmosphere - 1));
        var x1 = Math.Min(w - 1, (int)(cx + radius + atmosphere + 1));
        var y0 = Math.Max(0, (int)(cy - radius - atmosphere - 1));
        var y1 = Math.Min(h - 1, (int)(cy + radius + atmosphere + 1));

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = (x + 0.5 - cx) / radius;
                var dy = (y + 0.5 - cy) / radius;
                var d2 = dx * dx + dy * dy;
                var i = y * w + x;

                if (d2 > 1)
                {
                    // Atmosphere glow, stronger on the lit side.
                    var dist = (Math.Sqrt(d2) - 1) * radius;
                    if (dist > atmosphere)
                        continue;
                    var len = Math.Sqrt(d2);
                    var lit = Math.Max(0, (dx / len) * lx + (dy / len) * ly) / 0.79;
                    var a = (1 - dist / atmosphere) * (0.25 + 0.6 * lit);
                    a = Math.Floor(a * 3 + BayerAt(x, y) - 0.3) / 3;
                    if (a > 0)
                        px[i] = Blend(px[i], 0x5CE3C8, Math.Min(0.75, a));
                    continue;
                }

                var nz = Math.Sqrt(1 - d2);
                // Slight axial tilt.
                const double tilt = 0.32;
                var tx = dx * Math.Cos(tilt) - dy * Math.Sin(tilt);
                var ty = dx * Math.Sin(tilt) + dy * Math.Cos(tilt);
                var lon = Math.Atan2(tx, nz) + 0.6;
                var lat = Math.Asin(Math.Clamp(ty, -1, 1));

                // Voxel grid on the sphere: every block has one material.
                var bu = (int)Math.Floor((lon / Math.PI + 1) * 22);
                var bv = (int)Math.Floor((lat / Math.PI + 0.5) * 22);
                var e = Fbm(bu / 5.0, bv / 5.0, seed + 11);
                var jitter = (Hash01(seed + 12, bu, bv) - 0.5) * 0.08;

                uint color;
                var isLand = false;
                if (Math.Abs(lat) > 1.2 - jitter * 2)
                    color = 0xE6EEF4; // ice caps
                else if (e < 0.40)
                    color = 0x24438F; // deep ocean
                else if (e < 0.48)
                    color = 0x3563C2; // ocean
                else if (e < 0.515)
                    color = 0xD9C98A; // sand
                else if (e < 0.64)
                {
                    color = Hash01(seed + 13, bu, bv) > 0.5 ? 0x5FAA3Bu : 0x55A035u; // grass
                    isLand = true;
                }
                else if (e < 0.72)
                {
                    color = 0x3C7A2A; // forest
                    isLand = true;
                }
                else
                    color = e > 0.8 ? 0xEDEDEDu : 0x7E7E86u; // mountains / snow peaks

                color = Shade(color, 1 + jitter);

                // Clouds: blocky, on their own coarser grid.
                var cu = (int)Math.Floor((lon / Math.PI + 1) * 15 + 3);
                var cv = (int)Math.Floor((lat / Math.PI + 0.5) * 15);
                var cloud = Fbm(cu / 3.0 + 50, cv / 3.0, seed + 21) > 0.7;
                if (cloud)
                    color = Blend(color, 0xF4F7FA, 0.85);

                // Lighting in hard pixel-art bands.
                var diffuse = dx * lx + dy * ly + nz * lz;
                var shade = diffuse > 0.55 ? 1.0 : diffuse > 0.25 ? 0.82 : diffuse > 0.0 ? 0.6 : diffuse > -0.22 ? 0.38 : 0.2;
                var shaded = Shade(color, shade);
                if (shade < 0.4)
                {
                    shaded = Blend(shaded, 0x0B1024, 0.35);
                    // Village lights on the night side.
                    if (isLand && !cloud && Hash01(seed + 31, bu, bv) < 0.12)
                        shaded = Hash01(seed + 32, x, y) > 0.4 ? 0xFFC45Cu : 0xE89A3Au;
                }
                // Rim light on the lit edge.
                if (d2 > 0.9 && diffuse > 0.2)
                    shaded = Blend(shaded, 0xBDFCEB, 0.25);

                px[i] = shaded;
            }
        }
    }

    /// <summary>Isometric pixel-art cube with a stone texture and craters.</summary>
    private static void DrawCube(uint[] px, int w, int h, int cx, int cy, int s, uint baseColor, int seed, double dim)
    {
        const int texels = 6;
        for (var y = cy - s - 1; y <= cy + s + 1; y++)
        {
            for (var x = cx - s - 1; x <= cx + s + 1; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                    continue;
                double fx = x + 0.5 - cx, fy = y + 0.5 - cy;
                double a, b, shade;
                int face;

                // Top face: rhombus between (0,-s), (s,-s/2), (0,0), (-s,-s/2).
                var ta = (fx / s + 2 * (fy + s) / s) / 2;
                var tb = (2 * (fy + s) / s - fx / s) / 2;
                if (ta is >= 0 and <= 1 && tb is >= 0 and <= 1)
                {
                    a = ta; b = tb; shade = 1.0; face = 0;
                }
                else if (fx <= 0)
                {
                    // Left face: from (-s,-s/2) along (s, s/2) and down.
                    a = (fx + s) / s;
                    b = (fy + s / 2.0 - a * s / 2) / s;
                    shade = 0.74; face = 1;
                }
                else
                {
                    // Right face: from (0,0) along (s, -s/2) and down.
                    a = fx / s;
                    b = (fy + a * s / 2) / s;
                    shade = 0.52; face = 2;
                }
                if (a < 0 || a > 1 || b < 0 || b > 1)
                    continue;

                var u = Math.Min(texels - 1, (int)(a * texels));
                var v = Math.Min(texels - 1, (int)(b * texels));
                var noise = Hash01(seed + face * 7, u, v);
                var color = Shade(baseColor, (0.88 + noise * 0.22) * shade * dim);
                if (Hash01(seed + 40 + face, u, v) < 0.14)
                    color = Shade(color, 0.72); // crater
                // Dark outline on the silhouette.
                var edge = a < 0.06 || a > 0.94 || b > 0.94 || (face == 0 && (b < 0.06));
                if (edge)
                    color = Shade(color, 0.55);
                px[y * w + x] = color;
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static uint Shade(uint c, double k)
    {
        var r = (int)Math.Clamp(((c >> 16) & 0xFF) * k, 0, 255);
        var g = (int)Math.Clamp(((c >> 8) & 0xFF) * k, 0, 255);
        var b = (int)Math.Clamp((c & 0xFF) * k, 0, 255);
        return (uint)(r << 16 | g << 8 | b);
    }

    private static uint Blend(uint dst, uint src, double a)
    {
        var r = (int)(((dst >> 16) & 0xFF) * (1 - a) + ((src >> 16) & 0xFF) * a);
        var g = (int)(((dst >> 8) & 0xFF) * (1 - a) + ((src >> 8) & 0xFF) * a);
        var b = (int)((dst & 0xFF) * (1 - a) + (src & 0xFF) * a);
        return (uint)(r << 16 | g << 8 | b);
    }

    private static double Hash01(int seed, int x, int y)
    {
        unchecked
        {
            var hsh = (uint)seed * 0x9E3779B1u;
            hsh ^= (uint)x * 0x85EBCA77u;
            hsh = (hsh << 13) | (hsh >> 19);
            hsh ^= (uint)y * 0xC2B2AE3Du;
            hsh *= 0x27D4EB2Fu;
            hsh ^= hsh >> 15;
            hsh *= 0x165667B1u;
            hsh ^= hsh >> 13;
            return (hsh & 0xFFFFFF) / (double)0x1000000;
        }
    }

    private static double ValueNoise(double x, double y, int seed)
    {
        var xi = (int)Math.Floor(x);
        var yi = (int)Math.Floor(y);
        var xf = x - xi;
        var yf = y - yi;
        var u = xf * xf * (3 - 2 * xf);
        var v = yf * yf * (3 - 2 * yf);
        var a = Hash01(seed, xi, yi);
        var b = Hash01(seed, xi + 1, yi);
        var c = Hash01(seed, xi, yi + 1);
        var d = Hash01(seed, xi + 1, yi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static double Fbm(double x, double y, int seed)
    {
        double sum = 0, amp = 0.5, total = 0;
        for (var o = 0; o < 4; o++)
        {
            sum += ValueNoise(x, y, seed + o * 101) * amp;
            total += amp;
            x *= 2.03;
            y *= 2.03;
            amp *= 0.5;
        }
        return sum / total;
    }
}
