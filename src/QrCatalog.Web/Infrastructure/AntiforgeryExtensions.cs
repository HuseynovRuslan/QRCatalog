using Microsoft.AspNetCore.Antiforgery;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// JSON endpoint-lərində antiforgery-ni REAL yoxlayır. Metadata yanaşması işləmirdi:
/// framework IAntiforgeryMetadata-nı yalnız form-binding zamanı nəzərə alır, JSON
/// endpoint-lərdə heç nə validasiya olunmurdu (ilk CI run tapdı). Bu filter isə
/// ValidateRequestAsync-i özü çağırır — X-XSRF-TOKEN başlığı + cookie yoxlanılır.
/// </summary>
public static class AntiforgeryExtensions
{
    private sealed class AntiforgeryEndpointFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var antiforgery = context.HttpContext.RequestServices
                .GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery token yoxdur və ya etibarsızdır — səhifəni yeniləyib təkrar edin.");
            }

            return await next(context);
        }
    }

    public static TBuilder RequireAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(new AntiforgeryEndpointFilter());
}
