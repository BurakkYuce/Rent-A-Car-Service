using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.TabloDuzenleri;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.TabloDuzenleri;

/// <summary>
/// <c>/api/ui/v1/tablo-duzenleri/{tabloKodu}</c> (F3.5) — yeni arayüz tablo motorunun kişisel düzeni:
/// sütun sırası/görünürlüğü/genişliği + sıralama. <c>GET</c> kayıtlı düzeni (yoksa <c>duzen: null</c>, 404
/// DEĞİL), <c>PUT</c> upsert, <c>DELETE</c> varsayılana dönüş (204; kayıt yoksa da 204).
/// <para><b>İzin kapısı yok (bilinçli):</b> düzen iş verisi değil, oturumun kendi tercihi; kullanıcı
/// kimliği İSTEKTEN değil OTURUMDAN gelir (<see cref="TableLayoutService"/>) — uçta kullanıcı parametresi
/// yoktur, başkasının düzenine erişim ifade edilemez. Tenant izolasyonu EF filtresi + FORCE RLS.</para>
/// </summary>
public static class TabloDuzeniApi
{
    private const string Gerekce =
        "Kişisel tablo düzeni: kullanıcı kimliği istekten DEĞİL oturumdan gelir, her oturum yalnız KENDİ " +
        "düzenini okur/yazar; iş verisi ve kişisel veri taşımaz, izin matrisine bağlı değildir.";

    public static RouteGroupBuilder MapTabloDuzeniApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/tablo-duzenleri").WithTags("Tablo");

        g.MapGet("/{tabloKodu}", async Task<Ok<TabloDuzeniYaniti>> (string tabloKodu, TableLayoutService s, CancellationToken ct)
                => TypedResults.Ok(await s.FetchAsync(tabloKodu, ct)))
            .IzinMuaf(Gerekce);

        g.MapPut("/{tabloKodu}", async Task<Ok<TabloDuzeniYaniti>> (
                    string tabloKodu, TabloDuzeniVerisi govde, TableLayoutService s, CancellationToken ct)
                => TypedResults.Ok(await s.SaveAsync(tabloKodu, govde, ct)))
            .IzinMuaf(Gerekce);

        g.MapDelete("/{tabloKodu}", async Task<NoContent> (string tabloKodu, TableLayoutService s, CancellationToken ct) =>
            {
                await s.ResetAsync(tabloKodu, ct);
                return TypedResults.NoContent();
            })
            .IzinMuaf(Gerekce);

        return g;
    }
}
