# ── Frontend aktivləri: admin SPA + Tailwind CSS ──────────────────────────────
FROM node:22-alpine AS assets
WORKDIR /src

COPY admin/package.json admin/package-lock.json admin/
RUN cd admin && npm ci
COPY admin admin
RUN cd admin && npm run build

COPY src/QrCatalog.Web/package.json src/QrCatalog.Web/package-lock.json src/QrCatalog.Web/
RUN cd src/QrCatalog.Web && npm ci
COPY src/QrCatalog.Web/Styles src/QrCatalog.Web/Styles
# Tailwind v4 class-ları markup-dan avtomatik aşkarlayır — Razor səhifələri
# kopyalanmasa utility-lər CSS-ə düşmür və sayt stilsiz çıxır
COPY src/QrCatalog.Web/Pages src/QrCatalog.Web/Pages
RUN cd src/QrCatalog.Web && npm run build:css

# ── .NET build ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=assets /src/src/QrCatalog.Web/wwwroot/admin src/QrCatalog.Web/wwwroot/admin
COPY --from=assets /src/src/QrCatalog.Web/wwwroot/css src/QrCatalog.Web/wwwroot/css
RUN dotnet publish src/QrCatalog.Web -c Release -o /app

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Konteyner root olmayan istifadəçi ilə işləyir, /app isə root-a məxsusdur. Bu iki qovluq
# ƏVVƏLCƏDƏN yaradılıb sahibi dəyişdirilməlidir:
#  · uploads — LocalDiskStorage konstruktorda Directory.CreateDirectory çağırır; icazə
#    olmasa tətbiq startup-da yıxılır (Storage__Provider=Local defaultudur);
#  · keys — Data Protection açar dəstəsi (bax Program.cs).
# Named volume boş olanda Docker image-dəki məzmunu VƏ SAHİBLİYİ volume-a köçürür,
# ona görə volume da düzgün sahiblə gəlir.
RUN mkdir -p /app/uploads /app/keys && chown -R $APP_UID:$APP_UID /app/uploads /app/keys

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QrCatalog.Web.dll"]
