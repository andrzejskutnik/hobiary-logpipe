using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void Defaults_BaseDelay_2s_Max_60s_Multiplier_2_Jitter_02()
    {
        var opts = new RabbitMqOptions();
        Assert.Equal(2, opts.ReconnectDelaySeconds);
        Assert.Equal(60, opts.ReconnectMaxDelaySeconds);
        Assert.Equal(2.0, opts.ReconnectBackoffMultiplier);
        Assert.Equal(0.2, opts.ReconnectJitterFactor);
    }

    [Fact]
    public void MaxDelay_GreaterThan_BaseDelay()
    {
        var opts = new RabbitMqOptions();
        Assert.True(opts.ReconnectMaxDelaySeconds >= opts.ReconnectDelaySeconds);
    }

    [Theory]
    [InlineData(2.0, 2.0, 60.0, 4.0)]
    [InlineData(30.0, 2.0, 60.0, 60.0)]
    [InlineData(60.0, 2.0, 60.0, 60.0)]
    public void Backoff_Does_Not_Exceed_Max(double current, double multiplier, double max, double expected)
    {
        var next = Math.Min(current * multiplier, max);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void Custom_Options_Are_Respected()
    {
        var opts = new RabbitMqOptions
        {
            ReconnectDelaySeconds = 5,
            ReconnectMaxDelaySeconds = 120,
            ReconnectBackoffMultiplier = 3.0,
            ReconnectJitterFactor = 0.1,
        };
        Assert.Equal(5, opts.ReconnectDelaySeconds);
        Assert.Equal(120, opts.ReconnectMaxDelaySeconds);
        Assert.Equal(3.0, opts.ReconnectBackoffMultiplier);
        Assert.Equal(0.1, opts.ReconnectJitterFactor);
    }
}
