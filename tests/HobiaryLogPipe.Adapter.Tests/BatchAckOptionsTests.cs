using HobiaryLogPipe.Adapter;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class BatchAckOptionsTests
{
    [Fact]
    public void RabbitMqOptions_Defaults_BatchAck_50_200ms()
    {
        var opts = new RabbitMqOptions();
        Assert.Equal(50, opts.BatchAckSize);
        Assert.Equal(200, opts.BatchAckTimeoutMs);
    }

    [Fact]
    public void RabbitMqOptions_BatchAckSize_Zero_Disables()
    {
        var opts = new RabbitMqOptions { BatchAckSize = 0 };
        Assert.Equal(0, opts.BatchAckSize);
    }

    [Fact]
    public void RabbitMqOptions_Custom_Values()
    {
        var opts = new RabbitMqOptions { BatchAckSize = 100, BatchAckTimeoutMs = 500 };
        Assert.Equal(100, opts.BatchAckSize);
        Assert.Equal(500, opts.BatchAckTimeoutMs);
    }
}
