namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Bütün cavablara təhlükəsizlik başlıqları. CSP sərtdir — inline skript YOXDUR
/// (public.js ayrıca fayldır), nonce lazım deyil, output-cache ilə konflikt yaranmır.
/// img-src-ə R2 public domeni konfiqurasiyadan əlavə olunur (Storage:S3:PublicBaseUrl).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _csp;

    public SecurityHeadersMiddleware(
        RequestDelegate next, IConfiguration config, IWebHostEnvironment env)
    {
        _next = next;

        var imgSources = "'self' data:";
        var publicBase = config["Storage:S3:PublicBaseUrl"];
        if (Uri.TryCreate(publicBase, UriKind.Absolute, out var uri))
            imgSources += $" {uri.Scheme}://{uri.Host}";

        // Dev-də Scalar (/scalar) inline skript işlədir — yalnız orada yumşaldılır.
        // Produksiyada inline skript qadağandır: public.js ayrıca fayl olduğu üçün mümkündür.
        var scriptSrc = env.IsDevelopment() ? "script-src 'self' 'unsafe-inline'" : "script-src 'self'";

        _csp = string.Join("; ",
            "default-src 'self'",
            scriptSrc,
            // style attribute-ları (admin React, qalereya) üçün — inline <style> bloku yoxdur
            "style-src 'self' 'unsafe-inline'",
            $"img-src {imgSources}",
            "font-src 'self'",
            "connect-src 'self'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'");
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = _csp;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        return _next(context);
    }
}
