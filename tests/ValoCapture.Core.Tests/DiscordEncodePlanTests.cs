using ValoCapture.Core.Recording;

namespace ValoCapture.Core.Tests;

public sealed class DiscordEncodePlanTests
{
    private static readonly long Free = DiscordEncodePlan.LimitBytes(20);

    private static DiscordEncodePlan Plan(double seconds, long limit = 0, int width = 1920, int height = 1080, int fps = 60, int sourceKbps = 0) =>
        DiscordEncodePlan.Create(limit > 0 ? limit : Free, TimeSpan.FromSeconds(seconds), width, height, fps, sourceKbps)!;

    [Fact]
    public void ShortClip_KeepsFullResolutionAndCapsBitrate()
    {
        var plan = Plan(8);

        Assert.Equal((1920, 1080, 60), (plan.Width, plan.Height, plan.Framerate));
        Assert.Equal(DiscordEncodePlan.MaxVideoKbps, plan.VideoKbps);
    }

    [Fact]
    public void SourceBitrate_CapsVideoBitrate()
    {
        Assert.Equal(8_000, Plan(8, sourceKbps: 8_000).VideoKbps);
    }

    [Theory]
    [InlineData(30, 1280, 720, 60)]
    [InlineData(60, 960, 540, 60)]
    [InlineData(120, 854, 480, 30)]
    public void LongerClips_LowerResolutionAndFramerate(double seconds, int width, int height, int fps)
    {
        var plan = Plan(seconds);

        Assert.Equal((width, height, fps), (plan.Width, plan.Height, plan.Framerate));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(30)]
    [InlineData(120)]
    [InlineData(250)]
    public void EstimatedSize_FitsLimit(double seconds)
    {
        var plan = Plan(seconds);
        var bytes = (plan.VideoKbps + plan.AudioKbps) * 1000.0 / 8 * seconds;

        Assert.True(bytes <= Free * DiscordEncodePlan.TargetRatio, $"{bytes} > {Free}");
    }

    [Fact]
    public void TooLong_ReturnsNull()
    {
        var max = DiscordEncodePlan.MaxDuration(Free);

        Assert.NotNull(DiscordEncodePlan.Create(Free, max - TimeSpan.FromSeconds(1), 1920, 1080, 60));
        Assert.Null(DiscordEncodePlan.Create(Free, max + TimeSpan.FromSeconds(1), 1920, 1080, 60));
    }

    [Fact]
    public void NeverUpscales_AndKeepsAspectRatio()
    {
        var plan = Plan(8, width: 1280, height: 720);
        Assert.Equal((1280, 720), (plan.Width, plan.Height));

        var wide = Plan(30, width: 2560, height: 1080);
        Assert.Equal((1706, 720), (wide.Width, wide.Height));
    }

    [Fact]
    public void Shrink_LowersBitrateByOvershoot()
    {
        var plan = Plan(30);
        var shrunk = plan.Shrink(actualBytes: Free + Free / 10, limitBytes: Free);

        Assert.True(shrunk.VideoKbps < plan.VideoKbps * 0.85);
        Assert.Equal((plan.Width, plan.Height), (shrunk.Width, shrunk.Height));
    }
}
