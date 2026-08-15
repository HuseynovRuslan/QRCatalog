namespace QrCatalog.Infrastructure.Identity;

/// <summary>Rol adları — DB-də Identity rolları kimi saxlanılır. İstifadəçi bir müəssisəyə aiddir.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Editor, Viewer];
}

/// <summary>Custom claim açarları.</summary>
public static class AppClaims
{
    public const string CompanyId = "company_id";
    public const string DisplayName = "display_name";
}
