using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lumora.Launcher.App.Controls;

public enum SkinPreviewMode
{
    Front,
    Back,
    Head,
}

/// <summary>
/// Draws a Minecraft skin (64x64 or legacy 64x32) as a flat front/back figure or just the head,
/// with pixel-perfect scaling and the second (overlay) layer on top.
/// </summary>
public sealed class SkinPreview : Control
{
    public static readonly StyledProperty<Bitmap?> SkinProperty =
        AvaloniaProperty.Register<SkinPreview, Bitmap?>(nameof(Skin));

    public static readonly StyledProperty<bool> SlimProperty =
        AvaloniaProperty.Register<SkinPreview, bool>(nameof(Slim));

    public static readonly StyledProperty<SkinPreviewMode> ModeProperty =
        AvaloniaProperty.Register<SkinPreview, SkinPreviewMode>(nameof(Mode));

    public static readonly StyledProperty<IBrush?> PlaceholderBrushProperty =
        AvaloniaProperty.Register<SkinPreview, IBrush?>(nameof(PlaceholderBrush));

    static SkinPreview()
    {
        AffectsRender<SkinPreview>(SkinProperty, SlimProperty, ModeProperty, PlaceholderBrushProperty);
    }

    public Bitmap? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public bool Slim
    {
        get => GetValue(SlimProperty);
        set => SetValue(SlimProperty, value);
    }

    public SkinPreviewMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Fill for the silhouette when no skin is loaded.</summary>
    public IBrush? PlaceholderBrush
    {
        get => GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    // Figure is 16 x 32 "skin pixels"; the head alone is 8 x 8.
    private (double W, double H) Units => Mode == SkinPreviewMode.Head ? (8, 8) : (16, 32);

    protected override Size MeasureOverride(Size availableSize)
    {
        var (w, h) = Units;
        var scale = double.IsInfinity(availableSize.Height) ? 8 : availableSize.Height / h;
        if (!double.IsInfinity(availableSize.Width))
            scale = System.Math.Min(scale, availableSize.Width / w);
        return new Size(w * scale, h * scale);
    }

    public override void Render(DrawingContext context)
    {
        var (w, h) = Units;
        var scale = System.Math.Min(Bounds.Width / w, Bounds.Height / h);
        var ox = (Bounds.Width - w * scale) / 2;
        var oy = (Bounds.Height - h * scale) / 2;

        using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        var skin = Skin;
        if (skin is null)
        {
            DrawPlaceholder(context, scale, ox, oy);
            return;
        }

        var legacy = skin.PixelSize.Height == 32;
        var arm = Slim ? 3 : 4;

        void Part(int sx, int sy, int sw, int sh, double dx, double dy, double grow = 0)
        {
            var src = new Rect(sx, sy, sw, sh);
            var dst = new Rect(ox + (dx - grow) * scale, oy + (dy - grow) * scale, (sw + grow * 2) * scale, (sh + grow * 2) * scale);
            context.DrawImage(skin, src, dst);
        }

        if (Mode == SkinPreviewMode.Head)
        {
            Part(8, 8, 8, 8, 0, 0);
            Part(40, 8, 8, 8, 0, 0, 0.5);
            return;
        }

        var front = Mode == SkinPreviewMode.Front;
        // Arm x positions: on the front the player's right arm is on the viewer's left.
        var leftX = 4.0 - arm;
        var rightX = 12.0;

        if (front)
        {
            Part(8, 8, 8, 8, 4, 0);                                   // head
            Part(20, 20, 8, 12, 4, 8);                                // body
            Part(44, 20, arm, 12, leftX, 8);                          // right arm
            if (legacy) Part(44, 20, arm, 12, rightX, 8);             // legacy: mirrored copy
            else Part(36, 52, arm, 12, rightX, 8);                    // left arm
            Part(4, 20, 4, 12, 4, 20);                                // right leg
            if (legacy) Part(4, 20, 4, 12, 8, 20);
            else Part(20, 52, 4, 12, 8, 20);                          // left leg

            Part(40, 8, 8, 8, 4, 0, 0.5);                             // hat
            if (!legacy)
            {
                Part(20, 36, 8, 12, 4, 8, 0.25);                      // jacket
                Part(44, 36, arm, 12, leftX, 8, 0.25);                // right sleeve
                Part(52, 52, arm, 12, rightX, 8, 0.25);               // left sleeve
                Part(4, 36, 4, 12, 4, 20, 0.25);                      // right pants
                Part(4, 52, 4, 12, 8, 20, 0.25);                      // left pants
            }
        }
        else
        {
            var backArm = Slim ? 51 : 52;
            Part(24, 8, 8, 8, 4, 0);                                  // head back
            Part(32, 20, 8, 12, 4, 8);                                // body back
            Part(backArm, 20, arm, 12, rightX, 8);                    // right arm back (viewer's right)
            if (legacy) Part(backArm, 20, arm, 12, leftX, 8);
            else Part(backArm - 8, 52, arm, 12, leftX, 8);            // left arm back
            Part(12, 20, 4, 12, 8, 20);                               // right leg back
            if (legacy) Part(12, 20, 4, 12, 4, 20);
            else Part(28, 52, 4, 12, 4, 20);                          // left leg back

            Part(56, 8, 8, 8, 4, 0, 0.5);                             // hat back
            if (!legacy)
            {
                Part(32, 36, 8, 12, 4, 8, 0.25);
                Part(backArm, 36, arm, 12, rightX, 8, 0.25);
                Part(backArm + 8, 52, arm, 12, leftX, 8, 0.25);
                Part(12, 36, 4, 12, 8, 20, 0.25);
                Part(12, 52, 4, 12, 4, 20, 0.25);
            }
        }
    }

    private void DrawPlaceholder(DrawingContext context, double scale, double ox, double oy)
    {
        var brush = PlaceholderBrush ?? Brushes.Gray;
        void Box(double x, double y, double w, double h) =>
            context.DrawRectangle(brush, null, new Rect(ox + x * scale, oy + y * scale, w * scale, h * scale), scale * 0.6, scale * 0.6);

        if (Mode == SkinPreviewMode.Head)
        {
            Box(0, 0, 8, 8);
            return;
        }
        var arm = Slim ? 3 : 4;
        Box(4, 0, 8, 8);
        Box(4, 8.4, 8, 11.6);
        Box(4 - arm - 0.4, 8.4, arm, 11.6);
        Box(12.4, 8.4, arm, 11.6);
        Box(4, 20.4, 3.8, 11.6);
        Box(8.2, 20.4, 3.8, 11.6);
    }
}
