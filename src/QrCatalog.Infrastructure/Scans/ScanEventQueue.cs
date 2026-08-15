using System.Threading.Channels;

namespace QrCatalog.Infrastructure.Scans;

public sealed record PendingScan(Guid CompanyId, Guid QrCodeId, string DeviceKind, string? Lang);

/// <summary>
/// Skan hadisələrinin yaddaş növbəsi. Endpoint TryEnqueue edib dərhal qayıdır —
/// DB yazısı qonağın cavabını heç vaxt gözlətmir. Dolubsa (10k) yenilər atılır:
/// hücum altında statistika itirmək, səhifəni yavaşlatmaqdan ucuzdur.
/// </summary>
public sealed class ScanEventQueue
{
    private readonly Channel<PendingScan> _channel =
        Channel.CreateBounded<PendingScan>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public bool TryEnqueue(PendingScan scan) => _channel.Writer.TryWrite(scan);

    public ChannelReader<PendingScan> Reader => _channel.Reader;
}

/// <summary>User-Agent-dən kobud cihaz növü — dəqiq parsinq lazım deyil, hesabat üçündür.</summary>
public static class DeviceKindParser
{
    public static string Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "unknown";
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
            return "tablet";
        if (userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "mobile";
        return "desktop";
    }
}
