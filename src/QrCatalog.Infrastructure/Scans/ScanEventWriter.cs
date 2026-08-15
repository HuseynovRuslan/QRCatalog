using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;

namespace QrCatalog.Infrastructure.Scans;

/// <summary>
/// Növbədən oxuyub skanları toplu yazır. Bir hadisə gələn kimi yazılır; yük altında
/// növbədə yığılanlar bir SaveChanges-ə düşür (100-lük partiya). Xəta statistika
/// itirir, tətbiqi yıxmır.
/// </summary>
public sealed class ScanEventWriter : BackgroundService
{
    private const int MaxBatch = 100;

    private readonly ScanEventQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanEventWriter> _logger;

    public ScanEventWriter(
        ScanEventQueue queue, IServiceScopeFactory scopeFactory, ILogger<ScanEventWriter> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<PendingScan>(MaxBatch);

        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (batch.Count < MaxBatch && _queue.Reader.TryRead(out var scan))
                batch.Add(scan);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ScanEvents.AddRange(batch.Select(s =>
                    ScanEvent.Create(s.CompanyId, s.QrCodeId, s.DeviceKind, s.Lang)));
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Skan hadisələri yazıla bilmədi ({Count} ədəd itdi)", batch.Count);
            }
        }
    }
}
