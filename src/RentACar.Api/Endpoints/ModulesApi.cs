using RentACar.Api.Common;
using RentACar.Application.Authorization;
using RentACar.Application.Crm;
using RentACar.Application.Legal;
using RentACar.Application.Periods;
using RentACar.Application.Personnel;

namespace RentACar.Api.Endpoints;

/// <summary>
/// Yeni modüllerin salt-okunur JSON uçları (roadmap E1): personel (PII'siz), hukuk, CRM, dönem kapanışı
/// durumu. Tenant izolasyonu JWT→RLS ile otomatik. Entity sızdırmaz — sade DTO projeksiyonu döner.
/// Personel: TcKimlik/Maaş ASLA dönmez (PII).
/// </summary>
public static class ModulesApi
{
    public static IEndpointRouteBuilder MapModulesApi(this IEndpointRouteBuilder app)
    {
        // Personel — PII'siz liste; ManageUsers (yalnız Admin).
        app.MapGroup("/api/v1/personel").WithTags("Personel").RequirePermission(Permission.ManageUsers)
            .MapGet("/", async (PersonelService svc, CancellationToken ct) =>
                Results.Ok((await svc.ListAsync(ct)).Select(p => new
                {
                    p.Id, p.Kod, p.Ad, p.Soyad, p.Sube, p.Aktif, p.IseGiris, p.IseCikis, p.SurucuBelgeNo
                })));

        // Hukuk — OperationsWrite.
        app.MapGroup("/api/v1/legal").WithTags("Legal").RequirePermission(Permission.OperationsWrite)
            .MapGet("/", async (HukukDosyaService svc, CancellationToken ct) =>
                Results.Ok((await svc.ListAsync(ct)).Select(h => new
                {
                    h.Id, h.DosyaNo, Tur = h.Tur.ToString(), h.Avukat, h.Tutar, Durum = h.Durum.ToString(), h.Tarih, h.Aktif
                })));

        // CRM — OperationsWrite.
        var crm = app.MapGroup("/api/v1/crm").WithTags("Crm").RequirePermission(Permission.OperationsWrite);
        crm.MapGet("/anketler", async (AnketService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(a => new { a.Id, a.Puan, a.Yorum, a.Kaynak, a.Tarih })));
        crm.MapGet("/sikayetler", async (SikayetService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(s => new { s.Id, s.Konu, Durum = s.Durum.ToString(), s.Tarih, s.Cozum })));

        // Dönem kapanışı durumu — FinanceWrite.
        app.MapGroup("/api/v1/donem-kapanis").WithTags("DonemKapanis").RequirePermission(Permission.FinanceWrite)
            .MapGet("/", async (DonemKilidiService svc, CancellationToken ct) =>
                Results.Ok(new { kapanisTarihi = await svc.GetClosingDateAsync(ct) }));

        return app;
    }
}
