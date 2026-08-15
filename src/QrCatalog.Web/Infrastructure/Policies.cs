using QrCatalog.Infrastructure.Identity;

namespace QrCatalog.Web.Infrastructure;

/// <summary>Səlahiyyət policy-ləri — endpoint-lər rol adı ilə deyil, bunlarla qorunur.</summary>
public static class Policies
{
    public const string CanView = "CanView";
    public const string CanEdit = "CanEdit";
    public const string CanManage = "CanManage";

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(CanView, p => p.RequireRole(AppRoles.Admin, AppRoles.Editor, AppRoles.Viewer))
            .AddPolicy(CanEdit, p => p.RequireRole(AppRoles.Admin, AppRoles.Editor))
            .AddPolicy(CanManage, p => p.RequireRole(AppRoles.Admin));

        return services;
    }
}
