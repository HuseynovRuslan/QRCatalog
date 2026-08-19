using System.Collections.Concurrent;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Kod girişi üçün UĞURSUZ CƏHD sayğacı.
///
/// Niyə ayrıca mexanizm lazımdır: parol girişində Identity hesabı kilidləyir, çünki
/// e-poçt hansı hesab olduğunu deyir. Kod girişində isə səhv kod HEÇ BİR hesaba
/// aid deyil — kilidləyəcək hesab yoxdur. Qalan yeganə müdafiə cəhdlərin sayını
/// kəsməkdir.
///
/// Bu, qısa kodlarla («1655») kritik olur: 4 rəqəm = 10 min variant. IP üzrə limit
/// tək başına kifayət deyil — 100 IP-dən paylanmış cəhd onu keçir. Ona görə burada
/// QLOBAL pəncərə var: bütün sistemdə saatda N uğursuz cəhddən sonra kod girişi
/// müvəqqəti bağlanır. Real işçi öz kodunu yazır, ona görə uğursuz cəhd nadirdir;
/// 20 hədd gündəlik işə mane olmur, kobud gücü isə saatda 20-yə endirir
/// (10 min variant ≈ 3 həftə — kod isə bir toxunuşla dəyişdirilir).
///
/// Yaddaşdadır: tətbiq tək nüsxədir. Bir neçə nüsxəyə keçiləndə bu sayğac Redis-ə
/// köçməlidir, yoxsa hər nüsxə öz həddini ayrıca sayar və qoruma zəifləyər.
/// </summary>
public sealed class CodeLoginThrottle
{
    private const int GlobalLimit = 20;
    private const int PerIpLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentQueue<DateTime> _global = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _perIp = new();
    private readonly ILogger<CodeLoginThrottle> _logger;

    public CodeLoginThrottle(ILogger<CodeLoginThrottle> logger) => _logger = logger;

    /// <summary>Cəhd etməyə icazə varmı? <c>false</c> = hədd doldu.</summary>
    public bool IsAllowed(string ip)
    {
        Trim(_global);
        if (_global.Count >= GlobalLimit)
            return false;

        if (_perIp.TryGetValue(ip, out var attempts))
        {
            Trim(attempts);
            if (attempts.Count >= PerIpLimit)
                return false;
        }

        return true;
    }

    /// <summary>Uğursuz cəhdi qeyd edir. Uğurlu girişdə ÇAĞIRILMIR.</summary>
    public void RecordFailure(string ip)
    {
        var now = DateTime.UtcNow;
        _global.Enqueue(now);
        _perIp.GetOrAdd(ip, _ => new ConcurrentQueue<DateTime>()).Enqueue(now);

        Trim(_global);
        // Jurnalda görünsün: kimsə kod gəzirsə, bu sətirlər sıralanır
        _logger.LogWarning(
            "Kod girişi uğursuz. IP: {Ip}. Son bir saatda ümumi uğursuz cəhd: {Count}/{Limit}.",
            ip, _global.Count, GlobalLimit);

        if (_global.Count >= GlobalLimit)
            _logger.LogError(
                "Kod girişi MÜVƏQQƏTİ BAĞLANDI — bir saatda {Limit} uğursuz cəhd. " +
                "Kobud güc cəhdi ola bilər; kodları yeniləmək tövsiyə olunur.", GlobalLimit);
    }

    /// <summary>Uğurlu girişdən sonra həmin IP-nin sayğacı təmizlənir —
    /// düzgün kodu tapan adam yazı səhvinə görə cəzalanmasın.</summary>
    public void RecordSuccess(string ip) => _perIp.TryRemove(ip, out _);

    private static void Trim(ConcurrentQueue<DateTime> queue)
    {
        var cutoff = DateTime.UtcNow - Window;
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
            queue.TryDequeue(out _);
    }
}
