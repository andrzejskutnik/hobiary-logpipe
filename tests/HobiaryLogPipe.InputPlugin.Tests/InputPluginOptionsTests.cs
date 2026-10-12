using HobiaryLogPipe.InputPlugin;

namespace HobiaryLogPipe.InputPlugin.Tests;

public sealed class InputPluginOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new InputPluginOptions();

        Assert.Equal(LogPipeTransport.RabbitMq, opts.Transport);
        Assert.Equal(string.Empty, opts.PluginId);
        Assert.Equal("1.0", opts.Version);
        Assert.Equal("logs", opts.BaseLogDirectory);
        Assert.Equal("localhost", opts.Host);
        Assert.Equal(5672, opts.Port);
        Assert.Equal("/", opts.VirtualHost);
        Assert.Equal("guest", opts.Username);
        Assert.Equal("guest", opts.Password);
        Assert.Equal("hobiary.announce", opts.AnnounceExchange);
        Assert.Equal("hobiary.data", opts.DataExchange);
    }

    [Fact]
    public void DirectTransport_CanBeSelected()
    {
        var opts = new InputPluginOptions { Transport = LogPipeTransport.Direct };

        Assert.Equal(LogPipeTransport.Direct, opts.Transport);
    }

    [Fact]
    public void FieldMapping_DefaultNames_AreStandard()
    {
        var opts = new InputPluginOptions();

        Assert.Equal("level", opts.FieldMapping.Level);
        Assert.Equal("message", opts.FieldMapping.Message);
        Assert.Equal("source", opts.FieldMapping.Source);
        Assert.Equal("service", opts.FieldMapping.Service);
    }
}
