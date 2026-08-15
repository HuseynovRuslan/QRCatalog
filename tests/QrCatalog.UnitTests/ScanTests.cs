using QrCatalog.Infrastructure.Scans;

namespace QrCatalog.UnitTests;

public class DeviceKindParserTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148", "mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-A546B) Chrome/120 Mobile Safari", "mobile")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)", "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120", "desktop")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void Parse_ClassifiesUserAgent(string? userAgent, string expected)
    {
        Assert.Equal(expected, DeviceKindParser.Parse(userAgent));
    }
}

public class ScanEventQueueTests
{
    [Fact]
    public void TryEnqueue_AcceptsAndExposesViaReader()
    {
        var queue = new ScanEventQueue();
        var scan = new PendingScan(Guid.NewGuid(), Guid.NewGuid(), "mobile", "az");

        Assert.True(queue.TryEnqueue(scan));
        Assert.True(queue.Reader.TryRead(out var read));
        Assert.Equal(scan, read);
    }
}
