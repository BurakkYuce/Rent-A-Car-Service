using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Details;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Arac;

/// <summary>
/// <c>/api/ui/v1/araclar/*</c> — F6.1a araç uçları (para YOK). İş mantığı servislerde (<see cref="VehicleService"/>,
/// <see cref="DetailService"/>); burada uç kuralları: izin, şube kapsamı (kapsam kontrolü durum/varlık işinden ÖNCE),
/// sınırlar, sunucu tarafı sayfalama/sıralama, KVKK.
/// <list type="bullet">
/// <item><b>Okuma</b> (liste, özet, kart, detay, foto): OperationsWrite VEYA ViewReports (Blazor sayfası yalnız
/// <c>[Authorize]</c>; Muhasebe de görebiliyordu). Kapsamlı kullanıcı yalnız şubesinin araçlarını görür; başka şubenin
/// aracı 403, olmayan/başka kiracının aracı 404.</item>
/// <item><b>Yazma</b>: OperationsWrite; silme OperationsDelete (Blazor ile aynı). Hedef şube kapsamda olmalı.</item>
/// <item><b>PUT tam değiştirme</b>: zorunlu <c>surum</c>, uyuşmazlık 409 <c>cakisma</c>.</item>
/// </list>
/// </summary>
public static partial class AracApi
{
    private const string Kok = UiApiExtensions.V1 + "/araclar";

    /// <summary>(Sayfa − 1) × 200 int'i taşımasın (repo Skip int).</summary>
    private const int EnFazlaSayfa = 1_000_000;

    public static RouteGroupBuilder MapAracApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/araclar").WithTags("Araç");

        var oku = g.MapGroup("").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        oku.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        oku.MapGet("/ozet", Ozet);
        oku.MapGet("/model-gruplari", ModelGruplari);
        oku.MapGet("/{id:guid}", Kart);
        oku.MapGet("/{id:guid}/detay", Detay);

        g.MapGet("/detayli", Detayli).RequirePermission(Permission.ViewReports).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/durum", Durum).RequirePermission(Permission.OperationsWrite).AlanlariEsle(F5Ortak.SiralamaKurallari);

        var yaz = g.MapGroup("").RequirePermission(Permission.OperationsWrite);
        yaz.MapPost("", Olustur).AlanlariEsle(AracGirdi.Kurallar);
        yaz.MapPut("/{id:guid}", Guncelle).AlanlariEsle(AracGirdi.Kurallar);
        yaz.MapPost("/{id:guid}/km", KmGir).AlanlariEsle(KmKurallari);
        g.MapDelete("/{id:guid}", Sil).RequirePermission(Permission.OperationsDelete);

        MapFoto(g);
        MapSecim(g);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Araç bulunamadı.");

    // ================================================================== liste

    private static readonly SiralamaHaritasi<Vehicle> Harita = SiralamaHaritasi<Vehicle>
        .Olustur(v => v.Id)
        .Alan("plaka", v => v.Plaka)
        .Alan("marka", v => v.Marka)
        .Alan("tip", v => v.Tip)
        .Alan("detayTipi", v => v.DetayTipi)
        .Alan("grup", v => v.Grup)
        .Alan("segment", v => v.Segment)
        .Alan("sipp", v => v.Sipp)
        .Alan("modelYili", v => v.ModelYili)
        .Alan("renk", v => v.Renk)
        .Alan("km", v => v.Km)
        .Alan("sube", v => v.Sube)
        .Alan("durum", v => v.Durum)
        .Alan("filoDurum", v => v.FiloDurum)
        .Alan("aracSahibi", v => v.AracSahibi)
        .Alan("sonBakimKm", v => v.SonBakimKm)
        .Alan("alimBedeli", v => v.AlimBedeli)
        .Alan("filoGirisTarih", v => v.FiloGirisTarih)
        .Alan("filoCikisTarih", v => v.FiloCikisTarih)
        .Alan("tescilTarihi", v => v.TescilTarihi)
        .Alan("ikinciElDeger", v => v.IkinciElDeger)
        .Alan("tsbKaskoDegeri", v => v.TsbKaskoDegeri)
        .Alan("olusturma", v => v.CreatedAtUtc);

    /// <summary>Blazor <c>VehicleList</c> süzgeçleri. Tarih aralığı yalnız <c>tarihTuru</c> seçiliyken uygulanır (gün dahil).</summary>
    public sealed class AracListeFiltresi
    {
        /// <summary>Plaka / marka içinde geçen metin.</summary>
        [FromQuery(Name = "q")] public string? Q { get; set; }
        /// <summary>Grup adı ya da (<c>grupTuru=Sipp</c>) SIPP kodu.</summary>
        [FromQuery(Name = "grup")] public string? Grup { get; set; }
        /// <summary><c>Grup</c> (varsayılan) | <c>Sipp</c>.</summary>
        [FromQuery(Name = "grupTuru")] public string? GrupTuru { get; set; }
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        /// <summary><c>Hepsi</c> | <c>Girilmemis</c> (sahibi girilmemiş kovası).</summary>
        [FromQuery(Name = "sahiplik")] public string? Sahiplik { get; set; }
        [FromQuery(Name = "aracSahibi")] public string? AracSahibi { get; set; }
        /// <summary><c>Yok</c> | <c>FiloGiris</c> | <c>FiloCikis</c> | <c>Tescil</c>.</summary>
        [FromQuery(Name = "tarihTuru")] public string? TarihTuru { get; set; }
        [FromQuery(Name = "tarihBas")] public DateOnly? TarihBas { get; set; }
        [FromQuery(Name = "tarihBit")] public DateOnly? TarihBit { get; set; }

        public VehicleFilter ToFilter() => new()
        {
            Query = F5Ortak.Nz(Q),
            Grup = F5Ortak.Nz(Grup),
            GrupTuru = F5Ortak.EnumAdi<AracGrupTuru>(GrupTuru, "grupTuru") ?? AracGrupTuru.Grup,
            Durum = F5Ortak.EnumAdi<VehicleStatus>(Durum, "durum"),
            Sube = F5Ortak.Nz(Sube),
            Sahiplik = F5Ortak.EnumAdi<AracSahiplik>(Sahiplik, "sahiplik") ?? AracSahiplik.Hepsi,
            AracSahibi = F5Ortak.Nz(AracSahibi),
            TarihTuru = F5Ortak.EnumAdi<AracTarihTuru>(TarihTuru, "tarihTuru") ?? AracTarihTuru.Yok,
            // Repo sözleşmesi: TarihBit = bitiş GÜNÜNÜN başlangıç anı (repo +1 gün, strict <). İstanbul günü.
            TarihBas = TarihBas is { } b ? F5Ortak.GunBasi(b) : null,
            TarihBit = TarihBit is { } t ? F5Ortak.GunBasi(t) : null,
        };
    }

    private static async Task<Ok<Sayfa<AracListeSatiri>>> Liste(
        [AsParameters] AracListeFiltresi f, VehicleService araclar, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var istek = new ListeIstegi(Math.Min(sayfa ?? 1, EnFazlaSayfa), boyut ?? 50, sirala);
        var filtre = f.ToFilter();
        if (istek.Sirala is { } s)
        {
            Harita.Uygula(Array.Empty<Vehicle>().AsQueryable(), s); // bilinmeyen alan → 400 errors[sirala], sorgudan ÖNCE
            filtre.Siralama = q => Harita.Uygula(q, s);
        }
        filtre.Page = istek.Sayfa;
        filtre.PageSize = istek.Boyut;
        var sonuc = await araclar.SearchAsync(filtre, ct); // şube kapsamı serviste zorlanır
        var ek = await araclar.ListeEkAsync(sonuc.Items.Select(v => v.Id).ToList(), ct);
        var satirlar = sonuc.Items.Select(v => Satir(v, ek.GetValueOrDefault(v.Id))).ToList();
        return TypedResults.Ok(new Sayfa<AracListeSatiri>(satirlar, sonuc.Total, istek.Sayfa, istek.Boyut));
    }

    private static AracListeSatiri Satir(Vehicle v, VehicleListeEk? e) => new(
        v.Id, v.Plaka, v.Marka, v.Tip, v.DetayTipi, v.Grup, v.Segment, v.Sipp, v.ModelYili, v.Renk,
        v.Vites?.ToString(), v.Yakit?.ToString(), v.Km, v.Sube, v.Durum.ToString(), v.FiloDurum?.ToString(),
        v.SasiNo, v.MotorNo, v.MotorGucu, v.AracSahibi, v.HgsNo, v.OgsNo, v.KasaTipi, v.SonBakimKm,
        v.AlimBedeli, v.FiloGirisTarih, v.FiloCikisTarih, v.TescilTarihi, v.AlimYapilanFirma, v.RuhsatNo, v.IkinciElDeger,
        v.TsbKaskoDegeri, v.YedekAnahtar, v.KarLastigi, v.LastikDurumu, v.ZIzni, v.SonDurum, v.Konum, v.TakipNo,
        v.TeypKodu, v.Aciklama,
        e?.AktifKiraSozlesmeNo, e?.AcikServis ?? false, e?.AcikBaf ?? false, e?.SatisVar ?? false,
        e?.KaskoBitis, e?.KaskoPrim, e?.KrediKurulusu, e?.KrediSonTarih);

    /// <summary>Özet şerit — kapsamdaki TÜM filo (Blazor: Toplam/Müsait/Serviste/Doluluk; doluluk = müsait olmayan %).</summary>
    private static async Task<Ok<AracOzeti>> Ozet(VehicleService araclar, CancellationToken ct)
    {
        var tum = await araclar.ListAsync(ct);
        var musait = tum.Count(v => v.Durum == VehicleStatus.Musait);
        var doluluk = tum.Count == 0 ? 0 : (int)Math.Round(100.0 * (tum.Count - musait) / tum.Count);
        return TypedResults.Ok(new AracOzeti(tum.Count, musait, tum.Count(v => v.Durum == VehicleStatus.Serviste), doluluk));
    }

    /// <summary>"Modele göre grupla" (Marka+Tip): aynı filtreyle ilk 200 araç; 200 üstü <c>kirpildi</c> ile bildirilir.</summary>
    private static async Task<Ok<AracModelGruplari>> ModelGruplari(
        [AsParameters] AracListeFiltresi f, VehicleService araclar, CancellationToken ct)
    {
        var filtre = f.ToFilter();
        filtre.Page = 1;
        filtre.PageSize = 200;
        var sonuc = await araclar.SearchAsync(filtre, ct);
        var gruplar = sonuc.Items
            .GroupBy(v => $"{(v.Marka ?? "").Trim()}|{(v.Tip ?? "").Trim()}".ToLowerInvariant())
            .Select(g =>
            {
                var ilk = g.First();
                var etiket = $"{ilk.Marka} {ilk.Tip}".Trim();
                if (string.IsNullOrWhiteSpace(etiket)) etiket = "(model belirtilmemiş)";
                var yillar = g.Where(v => v.ModelYili is > 0).Select(v => v.ModelYili!.Value).ToList();
                var aralik = yillar.Count == 0 ? ""
                    : yillar.Min() == yillar.Max() ? yillar.Min().ToString() : $"{yillar.Min()}–{yillar.Max()}";
                return new AracModelGrubu(etiket, ilk.Grup, aralik, g.Count(),
                    g.Count(v => v.Durum == VehicleStatus.Musait), g.Count(v => v.Durum == VehicleStatus.Kirada),
                    g.Count(v => v.Durum == VehicleStatus.Serviste),
                    g.OrderByDescending(v => v.ModelYili ?? 0).ThenBy(v => v.Plaka, StringComparer.Ordinal)
                        .Select(v => new AracModelGrubuAraci(v.Id, v.Plaka, v.ModelYili, v.Renk, v.Vites?.ToString(),
                            v.Yakit?.ToString(), v.Km, v.Sube, v.Durum.ToString(), v.Sipp)).ToList());
            })
            .OrderByDescending(m => m.Toplam).ThenBy(m => m.Etiket, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new AracModelGruplari(sonuc.Total, sonuc.Total > 200, gruplar));
    }
}
