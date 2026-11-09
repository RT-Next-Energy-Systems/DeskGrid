using DeskGrid.Config;
using DeskGrid.Desktop;
using DeskGrid.Glass;
using DeskGrid.Panel;

namespace DeskGrid.Tests;

public sealed class LiquidGlassTests
{
    private static readonly ScreenRect Area = new(0, 0, 200, 160);
    private static readonly ScreenRect Window = new(40, 30, 160, 130); // 120 x 100
    private static readonly GlassStyle Clear = new(1, 20, 0, (0x20, 0x20, 0x20));

    private static ScreenImage Flat(byte gray)
    {
        var bgra = new byte[Area.Width * Area.Height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (gray, gray, gray, 255);
        }

        return new ScreenImage(Area, bgra);
    }

    // Brightness rises from left (dark) to right (bright), the same in every row.
    private static ScreenImage Ramp()
    {
        var bgra = new byte[Area.Width * Area.Height * 4];
        for (var y = 0; y < Area.Height; y++)
        {
            for (var x = 0; x < Area.Width; x++)
            {
                var i = (y * Area.Width + x) * 4;
                var value = (byte)(x * 255 / (Area.Width - 1));
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (value, value, value, 255);
            }
        }

        return new ScreenImage(Area, bgra);
    }

    private static (byte B, byte G, byte R, byte A) At(byte[] glass, int x, int y)
    {
        var i = (y * Window.Width + x) * 4;
        return (glass[i], glass[i + 1], glass[i + 2], glass[i + 3]);
    }

    [Fact]
    public void Render_IsTheSizeOfTheWindow_WithTransparentCornersAndAnOpaqueMiddle()
    {
        var glass = LiquidGlass.Render(Flat(128), Window, Clear);

        Assert.Equal(Window.Width * Window.Height * 4, glass.Length);
        Assert.Equal(0, At(glass, 0, 0).A);
        Assert.Equal(0, At(glass, Window.Width - 1, Window.Height - 1).A);
        Assert.Equal(255, At(glass, Window.Width / 2, Window.Height / 2).A);
        Assert.Equal(255, At(glass, Window.Width / 2, 0).A); // The middle of an edge is inside the rounded shape.
    }

    [Fact]
    public void Render_IsPremultiplied()
    {
        var glass = LiquidGlass.Render(Ramp(), Window, Clear with { Tint = 0.4 });

        for (var i = 0; i < glass.Length; i += 4)
        {
            Assert.True(glass[i] <= glass[i + 3] && glass[i + 1] <= glass[i + 3] && glass[i + 2] <= glass[i + 3]);
        }
    }

    [Fact]
    public void Render_ShowsTheScreenBehindItInTheMiddle_LightlyLifted()
    {
        var (b, g, r, _) = At(LiquidGlass.Render(Flat(128), Window, Clear), Window.Width / 2, Window.Height / 2);

        // Contrast and saturation are lifted a little, a flat gray stays gray.
        Assert.Equal(b, g);
        Assert.Equal(g, r);
        Assert.InRange(r, 125, 140);
    }

    [Fact]
    public void Render_TintMixesTheSurfaceColorIn()
    {
        var clear = At(LiquidGlass.Render(Flat(200), Window, Clear), Window.Width / 2, Window.Height / 2).R;
        var frosted = At(LiquidGlass.Render(Flat(200), Window, Clear with { Tint = 0.5 }), Window.Width / 2, Window.Height / 2).R;
        var solid = At(LiquidGlass.Render(Flat(200), Window, Clear with { Tint = 1 }), Window.Width / 2, Window.Height / 2).R;

        Assert.True(frosted < clear && frosted > solid);
        Assert.Equal(0x20, solid);
    }

    [Fact]
    public void Render_BendsTheBackdropAtTheRim_NotInTheMiddle()
    {
        var ramp = Ramp();
        var glass = LiquidGlass.Render(ramp, Window, Clear);
        int y = Window.Height / 2;

        // The rim shows what is further in: at the left edge that is brighter than the screen right there, and at
        // the right edge darker, while the middle is what is behind it.
        byte Behind(int x) => ramp.Bgra[((Window.Top + y) * Area.Width + Window.Left + x) * 4];
        Assert.True(At(glass, 2, y).R > Behind(2) + 5);
        Assert.True(At(glass, Window.Width - 3, y).R < Behind(Window.Width - 3) - 5);
        Assert.InRange(At(glass, Window.Width / 2, y).R, Behind(Window.Width / 2) - 8, Behind(Window.Width / 2) + 8);
    }

    // The rim is lit from the bottom left: the top right is the dark side, and the setting says how dark.
    [Fact]
    public void Render_DarkSide_FollowsTheShadeSetting_WhileTheBrightSideStays()
    {
        var screen = Flat(180);
        byte Corner(double dark, bool topRight)
        {
            var glass = LiquidGlass.Render(screen, Window, Clear with { DarkSide = dark });
            var (x, y) = topRight ? (Window.Width - 14, 6) : (6, Window.Height - 14);
            return At(glass, x, y).R;
        }

        Assert.True(Corner(0, topRight: true) > Corner(0.5, topRight: true));
        Assert.True(Corner(0.5, topRight: true) > Corner(1, topRight: true));
        Assert.Equal(Corner(0, topRight: false), Corner(1, topRight: false)); // The bright side is not the setting's business.
    }

    [Fact]
    public void TheLiquidGlassTheme_CarriesTheShadeSetting_DefaultingAndClamping()
    {
        PanelTheme Theme(double? shade) => PanelTheme.Resolve(
            new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass, GlassShade = shade }, false, true);

        Assert.Equal(PanelBackground.DefaultGlassShade, Theme(null).GlassShade);
        Assert.Equal(0.2, Theme(0.2).GlassShade);
        Assert.Equal(1, Theme(7).GlassShade);
        Assert.Equal(0, Theme(-1).GlassShade);
    }

    [Fact]
    public void Render_ABigPanelAndASmallOne_BothDraw()
    {
        // The window may be bigger than what was read (the panel on a smaller picture): the edge repeats, nothing throws.
        var big = LiquidGlass.Render(Flat(90), new ScreenRect(-50, -50, 300, 250), Clear);
        var tiny = LiquidGlass.Render(Flat(90), new ScreenRect(10, 10, 14, 13), Clear);

        Assert.Equal(350 * 300 * 4, big.Length);
        Assert.Equal(4 * 3 * 4, tiny.Length);
        Assert.Empty(LiquidGlass.Render(Flat(90), new ScreenRect(10, 10, 10, 20), Clear));
    }

    [Fact]
    public void Render_IsTheSameEveryTime()
    {
        Assert.Equal(LiquidGlass.Render(Ramp(), Window, Clear), LiquidGlass.Render(Ramp(), Window, Clear));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheLiquidGlassTheme_IsGlassFrostedWithTheSystemSurface_ByDefaultLightly(bool dark)
    {
        var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass }, dark, acrylicSupported: true);

        Assert.True(theme.Glass);
        Assert.False(theme.UseAcrylic);
        Assert.Equal(dark, theme.IsDark);
        Assert.Equal((byte)Math.Round(PanelTheme.DefaultOpacity(PanelBackgroundStyle.LiquidGlass) * 255), theme.Background.A);
        Assert.InRange(theme.Background.A, 40, 120);
    }

    [Fact]
    public void TheLiquidGlassTheme_UsesTheOpacityTheUserChoseAsTheFrosting()
    {
        var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass, Opacity = 0.6 }, false, true);

        Assert.Equal(153, theme.Background.A);
    }

    [Fact]
    public void Brightness_IsTheAverageOfWhatIsCovered_NotOfTheTransparentCorners()
    {
        // One half-covered white pixel and one fully covered black one, then plain transparent ones: (0.5 * 1 + 0) / 1.5.
        byte[] pixels = [128, 128, 128, 128, 0, 0, 0, 255, 0, 0, 0, 0];

        Assert.Equal(1.0 / 3, PanelGlass.Brightness(pixels), 0.01);
        Assert.Equal(0, PanelGlass.Brightness([0, 0, 0, 0]));
        Assert.Equal(1, PanelGlass.Brightness([255, 255, 255, 255]), 0.001);
    }

    [Fact]
    public void WithTextFor_PicksTextThatReadsOnTheBackground_KeepingTheRest()
    {
        var theme = PanelTheme.Resolve(new PanelBackground { Style = PanelBackgroundStyle.LiquidGlass }, systemDark: true, acrylicSupported: true);

        var onBright = theme.WithTextFor(brightBackground: true);
        var onDark = theme.WithTextFor(brightBackground: false);

        Assert.False(onBright.IsDark);
        Assert.True(onDark.IsDark);
        Assert.Equal(theme.Background, onBright.Background);
        Assert.True(onBright.Glass);
    }

    [Fact]
    public void OtherStyles_AreNotGlass()
    {
        foreach (var style in new[] { PanelBackgroundStyle.System, PanelBackgroundStyle.Dark, PanelBackgroundStyle.Light, PanelBackgroundStyle.Custom })
        {
            Assert.False(PanelTheme.Resolve(new PanelBackground { Style = style }, false, true).Glass);
        }
    }
}
