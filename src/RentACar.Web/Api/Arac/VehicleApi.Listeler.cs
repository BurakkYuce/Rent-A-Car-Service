using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Arac;

public static partial class VehicleApi
{
    // ================================================================== detaylı liste (ViewReports)

    private static readonly SortFieldMap<AracDetayliSatir> DetailedMap = SortFieldMap<AracDetayliSatir>
        .Create(s => s.Id)
        .Alan("plaka", s => s.Plaka)
        .Alan("marka", s => s.Marka)
        .Alan("grup", s => s.Grup)
        .Alan("sube", s => s.Sube)
        .Alan("durum", s => s.Durum)
        .Alan("alimTarihi", s => s.AlimTarihi)
        .Alan("alimBedeli", s => s.AlimBedeli)
        .Alan("muayeneBitis", s => s.MuayeneBitis)
        .Alan("kaskoBitis", s => s.KaskoBitis)
        .Alan("trafikBitis", s => s.TrafikBitis)
        .Alan("aktifKiraBitis", s => s.AktifKiraBitis)
        .Alan("filoGirisTarih", s => s.FiloGirisTarih);

    /// <summary>
    /// Detaylı araç listesi (Blazor <c>VehicleDetayList</c>). Servis bu listede kapsam uygulamıyor (Admin/Yönetici
    /// ekranı); izin kullanıcı bazında verilebildiği için uç ŞUBE KAPSAMINI yine uygular. Aktif kira müşterisi KVKK
    /// tek kuralıyla (AnonimAd) yeniden çözülür.
    /// </summary>
    private static async Task<Ok<Sayfa<AracDetayliSatir>>> Detailed(
        string? ara, string? sube, string? durum, int? sayfa, int? boyut, string? sirala,
        VehicleService araclar, ICurrentUser kullanici, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var rows = await araclar.ListDetailAsync(new VehicleDetayFilter
        {
            Ara = F5Shared.Nz(ara), Sube = F5Shared.Nz(sube),
            Durum = F5Shared.EnumAdi<VehicleStatus>(durum, "durum"), EnFazla = 10_000,
        }, ct);
        var scope = BranchScope.EffectiveFilter(kullanici);
        var visible = scope.Unrestricted ? rows
            : rows.Where(r => BranchScope.InScope(scope, r.Arac.SubeId, r.Arac.Sube)).ToList();
        var customers = await F5Shared.CustomersAsync(dbf,
            visible.Where(r => r.AktifKiraMusteriId is not null).Select(r => r.AktifKiraMusteriId!.Value), ct);
        var dtos = visible.Select(r =>
        {
            var v = r.Arac;
            var customer = r.AktifKiraMusteriId is { } m ? F5Shared.CustomerName(customers, m) : null;
            return new AracDetayliSatir(v.Id, v.Plaka, v.Marka, v.Tip, v.Grup, v.Sube, v.Durum.ToString(),
                v.BelgeNo, v.RuhsatSahibi, v.SozNo, v.AraciAlan, v.AlimYapilanFirma, v.AlimTarihi, v.AlimBedeli,
                v.AlisEuro == true, v.AlisEuroFiyat, v.SatisEuroFiyat,
                r.KrediBanka, r.MuayeneBitis, r.KaskoBitis, r.TrafikBitis,
                customer, r.AktifKiraSozlesmeNo, r.AktifKiraBitis,
                v.Kiralayan, v.KiraGun, v.KiraFiyat, v.KiraBitTar, v.KiraBekTar,
                v.SonTeslimKm, v.SonTeslimTarihi, v.AssistanFirma, v.HgsFirma, v.DisKmLimit,
                v.TsbKodu, v.TsbKaskoDegeri, v.OdemeSekli,
                r.SatisHedefFiyat, r.IhaleTarihi, r.IhaleFirmasi, r.NoterSatisTarihi,
                v.FiloGirisTarih, v.FiloCikisTarih, v.PasifSebep, v.SonDurum,
                v.OzelKod1, v.OzelKod2, v.OzelKod3, v.OzelKod4, v.OzelKod5);
        }).ToList();
        return TypedResults.Ok(F5Shared.Paginate(dtos, DetailedMap, Math.Min(sayfa ?? 1, MaxPage), boyut, sirala));
    }

    // ================================================================== araç güncel durum (OperationsWrite)

    private static readonly SortFieldMap<AracDurumSatiri> StatusMap = SortFieldMap<AracDurumSatiri>
        .Create(s => s.VehicleId)
        .Alan("plaka", s => s.Plaka)
        .Alan("marka", s => s.Marka)
        .Alan("grup", s => s.Grup)
        .Alan("durum", s => s.Durum)
        .Alan("filoDurum", s => s.FiloDurum)
        .Alan("kiraBitTar", s => s.KiraBitTar)
        .Alan("kiraKalanGun", s => s.KiraKalanGun)
        .Alan("kiraBakiye", s => s.KiraBakiye)
        .Alan("rezBasTar", s => s.RezBasTar)
        .Alan("km", s => s.Km)
        .Alan("sube", s => s.Sube);

    /// <summary>Blazor <c>FleetStatus</c> süzgeçleri (enum'lar ADLA; üçlü bayraklar <c>true</c>/<c>false</c>/boş).</summary>
    public sealed class VehicleStatusFilter
    {
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "filoDurum")] public string? FiloDurum { get; set; }
        [FromQuery(Name = "vites")] public string? Vites { get; set; }
        [FromQuery(Name = "yakit")] public string? Yakit { get; set; }
        [FromQuery(Name = "grup")] public string? Grup { get; set; }
        [FromQuery(Name = "marka")] public string? Marka { get; set; }
        [FromQuery(Name = "sube")] public string? Sube { get; set; }
        [FromQuery(Name = "kirada")] public bool? Kirada { get; set; }
        [FromQuery(Name = "pasifSebep")] public string? PasifSebep { get; set; }
        [FromQuery(Name = "hgs")] public string? Hgs { get; set; }
        [FromQuery(Name = "gps")] public string? Gps { get; set; }
        [FromQuery(Name = "karLastigi")] public bool? KarLastigi { get; set; }
        [FromQuery(Name = "webRezKapali")] public bool? WebRezKapali { get; set; }
        [FromQuery(Name = "ofisRezKapali")] public bool? OfisRezKapali { get; set; }

        public FleetStatusFilter ToFilter() => new()
        {
            Query = F5Shared.Nz(Q),
            Durum = F5Shared.EnumAdi<VehicleStatus>(Durum, "durum"),
            FiloDurum = F5Shared.EnumAdi<FleetLifecycleStatus>(FiloDurum, "filoDurum"),
            Vites = F5Shared.EnumAdi<Transmission>(Vites, "vites"),
            Yakit = F5Shared.EnumAdi<FuelType>(Yakit, "yakit"),
            Grup = F5Shared.Nz(Grup), Marka = F5Shared.Nz(Marka), Sube = F5Shared.Nz(Sube),
            KiradaMi = Kirada, PasifSebep = F5Shared.Nz(PasifSebep), HgsNo = F5Shared.Nz(Hgs), TakipNo = F5Shared.Nz(Gps),
            KarLastigi = KarLastigi, WebRezKapat = WebRezKapali, OfisRezKapat = OfisRezKapali,
        };
    }

    /// <summary>
    /// Araç güncel durum panosu. Kapsam serviste (operatör yalnız şubesi). Kira/rezervasyon müşteri adı ve telefonu
    /// <c>MusteriGorunumu</c> kuralıyla yeniden çözülür (repo'nun <c>DisplayName</c>'i AnonimAd bayrağını okumuyor).
    /// </summary>
    private static async Task<Ok<AracDurumYaniti>> Status(
        [AsParameters] VehicleStatusFilter f, int? sayfa, int? boyut, string? sirala,
        FleetStatusService durumlar, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var rows = await durumlar.QueryAsync(f.ToFilter(), ct);
        var customers = await F5Shared.CustomersAsync(dbf,
            rows.SelectMany(r => new[] { r.MusteriId, r.RezMusteriId }).Where(x => x is not null).Select(x => x!.Value), ct);
        var dtos = rows.Select(r =>
        {
            F5Shared.CariGorunum? m = r.MusteriId is { } mi && customers.TryGetValue(mi, out var a) ? a : null;
            string? res = r.RezMusteriId is { } ri ? F5Shared.CustomerName(customers, ri) : null;
            return new AracDurumSatiri(r.VehicleId, r.Plaka, r.Marka, r.Tip, r.Grup, r.FiloDurum?.ToString(),
                r.Durum.ToString(), r.AktifKiraId, r.KiraSozlesmeNo, r.MusteriId is null ? null : m?.Ad ?? "—", m?.CepTel,
                r.KiraBitTar, r.KiraKalanGun, r.KiraBakiye, res, r.RezBasTar, r.AcikServisNo, r.ServisAtolye,
                r.AktifBafNo, r.BafPersonelAd, r.DosyaNo, r.PasifSebep, r.Konum, r.TakipNo, r.HgsNo, r.KarLastigi,
                r.WebRezKapat, r.OfisRezKapat, r.Km, r.Sube, r.Kirada, r.Serviste, r.Bafta);
        }).ToList();
        var pageResult = F5Shared.Paginate(dtos, StatusMap, Math.Min(sayfa ?? 1, MaxPage), boyut, sirala);
        return TypedResults.Ok(new AracDurumYaniti(pageResult,
            dtos.Count(d => d.Kirada), dtos.Count(d => d.Serviste), dtos.Count(d => d.Bafta)));
    }
}
