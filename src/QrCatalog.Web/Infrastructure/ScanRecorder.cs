using QrCatalog.Infrastructure.Scans;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Skan qeydinin YEGANƏ yeri. Sistemdə iki fərqli yol var və hər biri öz səbəbinə görə lazımdır —
/// birini digəri ilə əvəz etmək olmaz:
///
/// <list type="bullet">
/// <item><b>Beacon</b> (<c>/api/public/scans</c>) — məhsul/arxiv/retired səhifəsi 200 qaytarır və
/// output-cache-lənir, ona görə handler təkrar skanlarda İŞLƏMİR. Qeyd brauzerdən gəlir;
/// əlavə fayda: JS işlətməyən botlar süzülür.</item>
/// <item><b>Server tərəfi</b> (Q səhifəsinin kateqoriya qolu) — 302 yönləndirmə keşlənmir
/// (output cache yalnız 200 saxlayır), ona görə handler HƏR skanda işləyir. Beacon burada
/// köməyə gəlmir: qonaq kataloq səhifəsinə keçir, orada isə token yoxdur.</item>
/// </list>
///
/// İkiqat sayma mümkün deyil: kateqoriya qolu yönləndirməklə bitir, beacon isə yalnız
/// <c>data-qr-token</c> daşıyan səhifələrdə işə düşür.
/// </summary>
public static class ScanRecorder
{
    /// <summary>
    /// Skanı növbəyə atır və dərhal qayıdır — DB yazısı qonağın cavabını gözlətmir.
    /// PII saxlanılmır: yalnız cihaz növü və dil teqi.
    /// </summary>
    public static void Record(ScanEventQueue queue, HttpContext http, Guid companyId, Guid qrCodeId) =>
        queue.TryEnqueue(new PendingScan(
            companyId,
            qrCodeId,
            DeviceKindParser.Parse(http.Request.Headers.UserAgent),
            FirstLanguage(http.Request.Headers.AcceptLanguage)));

    /// <summary>Accept-Language-in ilk teqi: "az", "ru-RU"… Şəxsi məlumat deyil.</summary>
    public static string? FirstLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;
        var first = acceptLanguage.Split(',')[0].Split(';')[0].Trim();
        return first.Length is > 0 and <= 10 ? first : null;
    }
}
