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
public static partial class VehicleApi
{
    private const string Root = UiApiExtensions.V1 + "/araclar";

    /// <summary>(Sayfa − 1) × 200 int'i taşımasın (repo Skip int).</summary>
    private const int MaxPage = 1_000_000;

    public static RouteGroupBuilder MapVehicleApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/araclar").WithTags("Araç");

        var read = g.MapGroup("").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        read.MapGet("", GetList).MapFields(F5Shared.SortRules);
        read.MapGet("/ozet", Summary);
        read.MapGet("/model-gruplari", ModelGroups);
        read.MapGet("/{id:guid}", Card);
        read.MapGet("/{id:guid}/detay", Detail);

        g.MapGet("/detayli", Detailed).RequirePermission(Permission.ViewReports).MapFields(F5Shared.SortRules);
        g.MapGet("/durum", Status).RequirePermission(Permission.OperationsWrite).MapFields(F5Shared.SortRules);

        var write = g.MapGroup("").RequirePermission(Permission.OperationsWrite);
        write.MapPost("", Create).MapFields(VehicleFormInput.Rules);
        write.MapPut("/{id:guid}", Update).MapFields(VehicleFormInput.Rules);
        write.MapPost("/{id:guid}/km", EnterKm).MapFields(KmRules);
        g.MapDelete("/{id:guid}", Delete).RequirePermission(Permission.OperationsDelete);

        MapPhoto(g);
        MapSelection(g);
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Araç bulunamadı.");

    // ================================================================== liste

    private static readonly SortFieldMap<Vehicle> Map = SortFieldMap<Vehicle>
        .Create(v => v.Id)
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
    public sealed class VehicleListFilter
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
            Query = F5Shared.Nz(Q),
            Grup = F5Shared.Nz(Grup),
            GrupTuru = F5Shared.EnumAdi<VehicleGroupType>(GrupTuru, "grupTuru") ?? VehicleGroupType.Grup,
            Durum = F5Shared.EnumAdi<VehicleStatus>(Durum, "durum"),
            Sube = F5Shared.Nz(Sube),
            Sahiplik = F5Shared.EnumAdi<VehicleOwnership>(Sahiplik, "sahiplik") ?? VehicleOwnership.Hepsi,
            AracSahibi = F5Shared.Nz(AracSahibi),
            TarihTuru = F5Shared.EnumAdi<VehicleDateType>(TarihTuru, "tarihTuru") ?? VehicleDateType.Yok,
            // Repo sözleşmesi: TarihBit = bitiş GÜNÜNÜN başlangıç anı (repo +1 gün, strict <). İstanbul günü.
            TarihBas = TarihBas is { } b ? F5Shared.DayStart(b, "tarihBas") : null,
            TarihBit = TarihBit is { } t ? F5Shared.DayStart(t, "tarihBit") : null,
        };
    }

    private static async Task<Ok<Sayfa<AracListeSatiri>>> GetList(
        [AsParameters] VehicleListFilter f, VehicleService araclar, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var request = new ListeIstegi(Math.Min(sayfa ?? 1, MaxPage), boyut ?? 50, sirala);
        var filter = f.ToFilter();
        if (request.Sirala is { } s)
        {
            Map.Apply(Array.Empty<Vehicle>().AsQueryable(), s); // bilinmeyen alan → 400 errors[sirala], sorgudan ÖNCE
            filter.Siralama = q => Map.Apply(q, s);
        }
        filter.Page = request.Sayfa;
        filter.PageSize = request.Boyut;
        var result = await araclar.SearchAsync(filter, ct); // şube kapsamı serviste zorlanır
        var extra = await araclar.ListExtrasAsync(result.Items.Select(v => v.Id).ToList(), ct);
        var rows = result.Items.Select(v => Row(v, extra.GetValueOrDefault(v.Id))).ToList();
        return TypedResults.Ok(new Sayfa<AracListeSatiri>(rows, result.Total, request.Sayfa, request.Boyut));
    }

    private static AracListeSatiri Row(Vehicle v, VehicleListeEk? e) => new(
        v.Id, v.Plaka, v.Marka, v.Tip, v.DetayTipi, v.Grup, v.Segment, v.Sipp, v.ModelYili, v.Renk,
        v.Vites?.ToString(), v.Yakit?.ToString(), v.Km, v.Sube, v.Durum.ToString(), v.FiloDurum?.ToString(),
        v.SasiNo, v.MotorNo, v.MotorGucu, v.AracSahibi, v.HgsNo, v.OgsNo, v.KasaTipi, v.SonBakimKm,
        v.AlimBedeli, v.FiloGirisTarih, v.FiloCikisTarih, v.TescilTarihi, v.AlimYapilanFirma, v.RuhsatNo, v.IkinciElDeger,
        v.TsbKaskoDegeri, v.YedekAnahtar, v.KarLastigi, v.LastikDurumu, v.ZIzni, v.SonDurum, v.Konum, v.TakipNo,
        v.TeypKodu, v.Aciklama,
        e?.AktifKiraSozlesmeNo, e?.AcikServis ?? false, e?.AcikBaf ?? false, e?.SatisVar ?? false,
        e?.KaskoBitis, e?.KaskoPrim, e?.KrediKurulusu, e?.KrediSonTarih);

    /// <summary>Özet şerit — kapsamdaki TÜM filo (Blazor: Toplam/Müsait/Serviste/Doluluk; doluluk = müsait olmayan %).</summary>
    private static async Task<Ok<AracOzeti>> Summary(VehicleService araclar, CancellationToken ct)
    {
        var all = await araclar.ListAsync(ct);
        var available = all.Count(v => v.Durum == VehicleStatus.Musait);
        var occupancy = all.Count == 0 ? 0 : (int)Math.Round(100.0 * (all.Count - available) / all.Count);
        return TypedResults.Ok(new AracOzeti(all.Count, available, all.Count(v => v.Durum == VehicleStatus.Serviste), occupancy));
    }

    /// <summary>"Modele göre grupla" (Marka+Tip): aynı filtreyle ilk 200 araç; 200 üstü <c>kirpildi</c> ile bildirilir.</summary>
    private static async Task<Ok<AracModelGruplari>> ModelGroups(
        [AsParameters] VehicleListFilter f, VehicleService araclar, CancellationToken ct)
    {
        var filter = f.ToFilter();
        filter.Page = 1;
        filter.PageSize = 200;
        var result = await araclar.SearchAsync(filter, ct);
        var groups = result.Items
            .GroupBy(v => $"{(v.Marka ?? "").Trim()}|{(v.Tip ?? "").Trim()}".ToLowerInvariant())
            .Select(g =>
            {
                var first = g.First();
                var label = $"{first.Marka} {first.Tip}".Trim();
                if (string.IsNullOrWhiteSpace(label)) label = "(model belirtilmemiş)";
                var years = g.Where(v => v.ModelYili is > 0).Select(v => v.ModelYili!.Value).ToList();
                var range = years.Count == 0 ? ""
                    : years.Min() == years.Max() ? years.Min().ToString() : $"{years.Min()}–{years.Max()}";
                return new AracModelGrubu(label, first.Grup, range, g.Count(),
                    g.Count(v => v.Durum == VehicleStatus.Musait), g.Count(v => v.Durum == VehicleStatus.Kirada),
                    g.Count(v => v.Durum == VehicleStatus.Serviste),
                    g.OrderByDescending(v => v.ModelYili ?? 0).ThenBy(v => v.Plaka, StringComparer.Ordinal)
                        .Select(v => new AracModelGrubuAraci(v.Id, v.Plaka, v.ModelYili, v.Renk, v.Vites?.ToString(),
                            v.Yakit?.ToString(), v.Km, v.Sube, v.Durum.ToString(), v.Sipp)).ToList());
            })
            .OrderByDescending(m => m.Toplam).ThenBy(m => m.Etiket, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new AracModelGruplari(result.Total, result.Total > 200, groups));
    }
}
