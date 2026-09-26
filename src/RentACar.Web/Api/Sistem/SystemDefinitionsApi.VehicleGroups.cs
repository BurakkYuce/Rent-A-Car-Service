using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// Araç grubu tanımı + grup-düzeyi fiyat kuralları + filo eşleme aracı (Blazor <c>VehicleGroupList</c> paritesi).
/// OperationsWrite. Ad değişince araçların <c>Grup</c> metni AYNI işlemde taşınır (rename cascade; sürümlü PUT'ta da).
/// </summary>
public static partial class SystemDefinitionsApi
{
    private static readonly (string, string)[] GroupRules =
    [
        ("Araç grubu kodu", "kod"), ("Araç grubu adı", "ad"), ("'", "kod"), ("SIPP", "sipp"),
        ("Koltuk sayısı", "koltukSayisi"), ("Kapı sayısı", "kapiSayisi"), ("Bagaj sayısı", "bagajSayisi"),
        ("Küçük bagaj", "kucukBagaj"), ("Büyük bagaj", "buyukBagaj"), ("Günlük KM", "gunlukKmLimiti"),
        ("Aylık max KM", "aylikMaxKm"), ("Web sıra", "webSira"), ("Upgrade sıra", "upgradeSira"),
        ("Provizyon 2", "provizyon2"), ("Provizyon", "provizyon"), ("Muafiyet tutarı", "muafiyetTutari"),
        ("Muafiyet 2", "muafiyet2"), ("Aşım KM", "asimKmUcreti"), ("Yakıt fiyatı", "yakitFiyati"),
        ("Genç sürücü ücreti", "gencSurucuUcretGunluk"), ("Ek sürücü ücreti", "ekSurucuUcretGunluk"),
        ("Sürücü min", "surucuMinYas"), ("Genç sürücü yaşı", "gencSurucuYas"), ("Ehliyet min", "ehliyetMinYil"),
        ("Genç ehliyet", "gencEhliyetMinYil"), ("Sonra öde", "sonraOdeOran"),
    ];

    private static readonly (string, string)[] AssignRules =
        [("Kaynak grup değeri", "kaynak"), ("Hedef araç grubu", "hedefGrupId"), ("'", "hedefGrupId")];

    private static readonly SortFieldMap<VehicleGroupDto> GroupSort = SortFieldMap<VehicleGroupDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("segment", x => x.Segment)
        .Alan("webSira", x => x.WebSira).Alan("aracSayisi", x => x.AracSayisi).Alan("aktif", x => x.Aktif);

    private static void MapVehicleGroups(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/arac-gruplari").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, VehicleGroupService s, CancellationToken ct) =>
        {
            var counts = await s.VehicleCountsAsync(ct);
            var rows = (await s.ListAsync(ct)).Select(x => VehicleGroupDto.From(x, counts.GetValueOrDefault(x.Id), null));
            return TypedResults.Ok(F5Ortak.Sayfala(
                Filter(rows, ara, aktif, x => [x.Kod, x.Ad, x.Sipp, x.Segment], x => x.Aktif), GroupSort, sayfa, boyut, sirala));
        }).AlanlariEsle(F5Ortak.SiralamaKurallari);
        // Tanılama paneli: hiçbir aktif gruba eşleşmeyen filo Grup değerleri (+ grubu boş araçlar).
        g.MapGet("/eslesmeyen", async Task<Ok<IReadOnlyList<UnmatchedGroupValueDto>>> (VehicleGroupService s, CancellationToken ct)
            => TypedResults.Ok<IReadOnlyList<UnmatchedGroupValueDto>>((await s.ListUnmatchedGroupValuesAsync(ct))
                .Select(x => new UnmatchedGroupValueDto(x.Grup, x.AracSayisi, x.Bos)).ToList()));
        g.MapGet("/{id:guid}", async Task<Results<Ok<VehicleGroupDto>, ProblemHttpResult>> (Guid id, VehicleGroupService s, CancellationToken ct)
            => await GroupAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<VehicleGroupDto>, ProblemHttpResult>> (VehicleGroupRequest i, VehicleGroupService s, CancellationToken ct) =>
        {
            var input = GroupInput(i);
            var id = await s.CreateAsync(input, ct);
            return await GroupAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/arac-gruplari/{id}", d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(GroupRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<VehicleGroupDto>, ProblemHttpResult>> (Guid id, VehicleGroupRequest i, VehicleGroupService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            var input = GroupInput(i);
            if (!await s.UpdateAsync(id, input, i.Surum, ct)) return SystemApiCommon.NotFound();
            return await GroupAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(GroupRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, VehicleGroupService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
        // Eşleme aracı: eşleşmeyen serbest-metin Grup değerini (ya da grubu boş araçları) tanımlı gruba taşır.
        g.MapPost("/ata", async Task<Results<Ok<GroupAssignResult>, ProblemHttpResult>> (GroupAssignRequest i, VehicleGroupService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(i.HedefGrupId, ct) is null) return SystemApiCommon.NotFound("Hedef araç grubu bulunamadı.");
            Text(i.Kaynak, 64, "kaynak", "Kaynak grup değeri");
            return TypedResults.Ok(new GroupAssignResult(await s.AssignGroupValueAsync(i.Kaynak, i.Bos, i.HedefGrupId, ct)));
        }).AlanlariEsle(AssignRules);
    }

    /// <summary>Uç sınırları (kolon uzunlukları + numeric(19,4)) + enum adları; iş kuralları serviste.</summary>
    private static VehicleGroupInput GroupInput(VehicleGroupRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Text(i.Aciklama, 512, "aciklama", "Açıklama");
        Text(i.Sipp, 8, "sipp", "SIPP");
        Text(i.Segment, 64, "segment", "Segment");
        Text(i.KasaTuru, 32, "kasaTuru", "Kasa türü");
        Text(i.Marka, 64, "marka", "Marka");
        Text(i.Tipi, 64, "tipi", "Tipi");
        Text(i.ProvizyonDoviz, 3, "provizyonDoviz", "Provizyon dövizi");
        Text(i.Provizyon2Doviz, 3, "provizyon2Doviz", "Provizyon 2 dövizi");
        Text(i.EntegrasyonKod1, 64, "entegrasyonKod1", "Entegrasyon kodu");
        Text(i.WebId, 64, "webId", "Web Id");
        Text(i.ServisId, 64, "servisId", "Servis Id");
        Amount(i.GencSurucuUcretGunluk, "gencSurucuUcretGunluk", "Genç sürücü ücreti");
        Amount(i.EkSurucuUcretGunluk, "ekSurucuUcretGunluk", "Ek sürücü ücreti");
        Amount(i.Provizyon, "provizyon", "Provizyon");
        Amount(i.Provizyon2, "provizyon2", "Provizyon 2");
        Amount(i.MuafiyetTutari, "muafiyetTutari", "Muafiyet tutarı");
        Amount(i.Muafiyet2, "muafiyet2", "Muafiyet 2");
        Amount(i.AsimKmUcreti, "asimKmUcreti", "Aşım KM ücreti");
        Amount(i.YakitFiyati, "yakitFiyati", "Yakıt fiyatı");
        return new VehicleGroupInput
        {
            Kod = i.Kod ?? "", Ad = i.Ad ?? "", Aciklama = i.Aciklama, Sipp = i.Sipp, Segment = i.Segment,
            KasaTuru = i.KasaTuru, Marka = i.Marka, Tipi = i.Tipi, KoltukSayisi = i.KoltukSayisi, KapiSayisi = i.KapiSayisi,
            BagajSayisi = i.BagajSayisi, KucukBagaj = i.KucukBagaj, BuyukBagaj = i.BuyukBagaj, SurucuMinYas = i.SurucuMinYas,
            GencSurucuYas = i.GencSurucuYas, GencSurucuUcretGunluk = i.GencSurucuUcretGunluk,
            EkSurucuUcretGunluk = i.EkSurucuUcretGunluk, EhliyetMinYil = i.EhliyetMinYil,
            GencEhliyetMinYil = i.GencEhliyetMinYil, Provizyon = i.Provizyon, Provizyon2 = i.Provizyon2,
            MuafiyetTutari = i.MuafiyetTutari, Muafiyet2 = i.Muafiyet2, GunlukKmLimiti = i.GunlukKmLimiti,
            AylikMaxKm = i.AylikMaxKm, AsimKmUcreti = i.AsimKmUcreti, YakitFiyati = i.YakitFiyati,
            SonraOdeOran = i.SonraOdeOran, KrediKartiSart = i.KrediKartiSart, WebSira = i.WebSira, UpgradeSira = i.UpgradeSira,
            ProvizyonDoviz = i.ProvizyonDoviz, Provizyon2Doviz = i.Provizyon2Doviz,
            YakitTuru = EnumName<FuelType>(i.YakitTuru, "yakitTuru"), Vites = EnumName<Transmission>(i.Vites, "vites"),
            EntegrasyonKod1 = i.EntegrasyonKod1, WebId = i.WebId, ServisId = i.ServisId, Aktif = i.Aktif,
        };
    }

    private static async Task<VehicleGroupDto?> GroupAsync(Guid id, VehicleGroupService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        if (await s.GetAsync(id, ct) is not { } x) return null;
        var counts = await s.VehicleCountsAsync(ct);
        return VehicleGroupDto.From(x, counts.GetValueOrDefault(x.Id), version);
    }
}
