using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;

namespace QrCatalog.Infrastructure.Identity;

public static class BootstrapSeeder
{
    /// <summary>
    /// İlk işə salınma: rollar + (konfiqurasiya varsa) ilk müəssisə və admin.
    /// İdempotentdir — admin artıq varsa heç nə etmir, env dəyişənləri açıq qala bilər.
    /// </summary>
    public static async Task SeedBootstrapAdminAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

        try
        {
            var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var role in AppRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new ApplicationRole(role));
            }

            var config = sp.GetRequiredService<IConfiguration>();
            var email = config["Bootstrap:AdminEmail"];
            var password = config["Bootstrap:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return; // bootstrap konfiqurasiyası yoxdur — normal haldır

            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByEmailAsync(email) is not null)
                return; // artıq mövcuddur

            var db = sp.GetRequiredService<AppDbContext>();
            var slug = config["Bootstrap:CompanySlug"] ?? "esas";

            // İKİNCİ QORUYUCU — e-poçt kifayət etmir.
            //
            // Seeder hər başlanğıcda işləyir və idempotentliyi YALNIZ e-poçta bağlı idi.
            // İstifadəçi paneldən öz e-poçtunu düzəldəndə (məs. səhv yazılmış ünvanı)
            // növbəti deploy həmin ünvanı tapmır və müəssisəyə YENİ tam səlahiyyətli
            // admin yaradır — paroluysa `.env`-dəki köhnə, hamıya məlum dəyər olur.
            // Hesab istifadəçilər siyahısında birdən peyda olur, izahı isə görünmür.
            //
            // Ona görə şərt genişlənir: bu müəssisədə HEÇ BİR aktiv admin yoxdursa
            // seed edilir. Boş sistemdə (ilk quraşdırma) davranış dəyişmir.
            var existingCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug);
            if (existingCompany is not null)
            {
                var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
                if (admins.Any(u => u.CompanyId == existingCompany.Id))
                {
                    logger.LogInformation(
                        "Bootstrap ötürüldü: «{Company}» müəssisəsində artıq admin var. " +
                        "Bootstrap:AdminEmail dəyəri ({Email}) bazadaki ünvandan fərqlidirsə " +
                        "bu normaldır — hesab paneldən dəyişdirilib.", existingCompany.Name, email);
                    return;
                }
            }
            // Company tenant-owned deyil — filtr yoxdur, birbaşa sorğulamaq olar
            var company = existingCompany;
            if (company is null)
            {
                company = Company.Create(config["Bootstrap:CompanyName"] ?? "Əsas müəssisə", slug);
                db.Companies.Add(company);
                await db.SaveChangesAsync();
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CompanyId = company.Id,
                DisplayName = "Admin",
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Bootstrap admin yaradıla bilmədi: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            await userManager.AddToRoleAsync(user, AppRoles.Admin);
            logger.LogInformation("Bootstrap admin yaradıldı: {Email} → {Company}", email, company.Name);
        }
        catch (Exception ex)
        {
            // Baza əlçatan deyilsə tətbiq yenə qalxsın — problem /health-də görünür
            logger.LogError(ex, "Bootstrap seed alınmadı — baza əlçatan olmaya bilər.");
        }
    }
}
