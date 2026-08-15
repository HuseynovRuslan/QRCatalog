using Microsoft.AspNetCore.Antiforgery;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// JSON endpoint-lərində antiforgery validasiyasını açır. Framework bunu form endpoint-lərinə
/// avtomatik qoşur, JSON üçün isə açıq metadata lazımdır — UseAntiforgery middleware
/// <see cref="IAntiforgeryMetadata.RequiresValidation"/> = true görəndə yoxlayır.
/// </summary>
public static class AntiforgeryExtensions
{
    private sealed class RequireAntiforgeryMetadata : IAntiforgeryMetadata
    {
        public bool RequiresValidation => true;
    }

    public static TBuilder RequireAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new RequireAntiforgeryMetadata());
}
