using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// Ofis/lokasyon tanımı. OperationsWrite (Blazor paritesi). Liste tüm ofisleri döner (form açılır listeleri
/// ve müsaitlik ofis adlarını buradan okur). YAZMA şube kapsamından geçer: ofisin şubesi, çıkış ofisi →
/// Location → türetilmiş şube zincirinin (<c>OfficeBranchInterceptor</c>) kaynağıdır; şubeye bağlı operatör başka
/// şubenin ofisini değiştirip/silip ya da bir ofisi kendi şubesine taşıyıp kapsamı büyütemez. Blazor ekranında bu
/// kontrol yoktu; yeni yüzey o açığı taşımaz (F5Ortak.CikisOfisiKapsamiAsync ile aynı gerekçe).
/// </summary>
public static partial class SystemDefinitionsApi
{
    private static readonly (string, string)[] LocationRules =
        [("Ofis kodu", "kod"), ("Ofis adı", "ad"), ("'", "kod")];

    private static readonly SiralamaHaritasi<LocationDto> LocationSort = SiralamaHaritasi<LocationDto>
        .Olustur(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("sube", x => x.Sube)
        .Alan("webSira", x => x.WebSira).Alan("aktif", x => x.Aktif);

    private static void MapLocations(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/lokasyonlar").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, string? sube, LocationService s, CancellationToken ct) =>
        {
            IEnumerable<Location> rows = await s.ListAsync(ct);
            if (SystemApiCommon.Clean(sube) is { } b) rows = rows.Where(x => TurkishText.EqualsIgnoreTurkishCase(x.Sube, b));
            return TypedResults.Ok(F5Ortak.Sayfala(
                Filter(rows.Select(x => LocationDto.From(x, null)), ara, aktif, x => [x.Kod, x.Ad, x.Sube, x.Iata], x => x.Aktif),
                LocationSort, sayfa, boyut, sirala));
        }).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", async Task<Results<Ok<LocationDto>, ProblemHttpResult>> (Guid id, LocationService s, CancellationToken ct)
            => await LocationAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<LocationDto>, ProblemHttpResult>> (LocationRequest i, LocationService s, ICurrentUser user, CancellationToken ct) =>
        {
            LocationLimits(i);
            BranchScope.RequireInScope(user, kayitSubeId: null, SystemApiCommon.Clean(i.Sube));
            var id = await s.CreateAsync(LocationInput(i), ct);
            return await LocationAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/lokasyonlar/{id}", d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(LocationRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<LocationDto>, ProblemHttpResult>> (Guid id, LocationRequest i, LocationService s, ICurrentUser user, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is not { } current) return SystemApiCommon.NotFound();
            BranchScope.RequireInScope(user, current.SubeId, current.Sube); // kapsam durumdan/doğrulamadan ÖNCE
            SystemApiCommon.RequireVersion(i.Surum);
            LocationLimits(i);
            BranchScope.RequireInScope(user, kayitSubeId: null, SystemApiCommon.Clean(i.Sube));
            if (!await s.UpdateAsync(id, LocationInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await LocationAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(LocationRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, LocationService s, ICurrentUser user, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is not { } current) return SystemApiCommon.NotFound();
            BranchScope.RequireInScope(user, current.SubeId, current.Sube);
            return await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound();
        });
    }

    private static void LocationLimits(LocationRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Text(i.Adres, 512, "adres", "Adres");
        Text(i.Telefon, 32, "telefon", "Telefon");
        Text(i.Eposta, 128, "eposta", "E-posta");
        Text(i.CalismaSaatleri, 64, "calismaSaatleri", "Çalışma saatleri");
        Amount(i.TeslimUcreti, "teslimUcreti", "Teslim ücreti");
        if (i.TeslimUcreti is < 0m) throw new ValidationException("Teslim ücreti negatif olamaz.", "teslimUcreti");
        Text(i.Sube, 64, "sube", "Şube");
        Text(i.IngilizceAd, 128, "ingilizceAd", "İngilizce ad");
        Text(i.BulusmaNoktasi, 128, "bulusmaNoktasi", "Buluşma noktası");
        Text(i.Iata, 8, "iata", "IATA");
        Text(i.LokasyonTuru, 64, "lokasyonTuru", "Lokasyon türü");
        Text(i.BinaNo, 32, "binaNo", "Bina no");
        Text(i.Tarif, 1024, "tarif", "Tarif");
        Text(i.Ulke, 64, "ulke", "Ülke");
        Text(i.PostaKodu, 16, "postaKodu", "Posta kodu");
        Text(i.MapsKonumu, 256, "mapsKonumu", "Harita konumu");
        Text(i.EkAciklama, 1024, "ekAciklama", "Ek açıklama");
        NotNegative(i.WebSira, "webSira", "Web sıra");
        Text(i.DropKarsilamaTuru, 64, "dropKarsilamaTuru", "Drop karşılama türü");
        Text(i.DropCalismaSekli, 64, "dropCalismaSekli", "Drop çalışma şekli");
        Text(i.OzelMail, 128, "ozelMail", "Özel mail");
        Text(i.OzelTelefon, 32, "ozelTelefon", "Özel telefon");
        // Haftalık saatler JSON olarak varchar(1024) kolonda: satır sayısı ve saat metni sınırlı (7 × kısa saat).
        var hours = i.HaftalikCalismaSaatleri ?? [];
        if (hours.Count > 7)
            throw new ValidationException("Haftalık çalışma saatleri en fazla 7 gün içerebilir.", "haftalikCalismaSaatleri");
        foreach (var h in hours)
        {
            if (h.Gun is < 1 or > 7)
                throw new ValidationException("Gün 1 (Pazartesi) ile 7 (Pazar) arasında olmalıdır.", "haftalikCalismaSaatleri");
            if (h.Acilis is { Length: > 8 } || h.Kapanis is { Length: > 8 })
                throw new ValidationException("Saat biçimi SS:dd olmalıdır.", "haftalikCalismaSaatleri");
        }
    }

    private static LocationInput LocationInput(LocationRequest i) => new()
    {
        Kod = i.Kod ?? "", Ad = i.Ad ?? "", Adres = i.Adres, Telefon = i.Telefon, Eposta = i.Eposta,
        CalismaSaatleri = i.CalismaSaatleri, TeslimUcreti = i.TeslimUcreti, Sube = i.Sube, IngilizceAd = i.IngilizceAd,
        BulusmaNoktasi = i.BulusmaNoktasi, Iata = i.Iata, WebdeGizle = i.WebdeGizle, LokasyonTuru = i.LokasyonTuru,
        BinaNo = i.BinaNo, Tarif = i.Tarif, Ulke = i.Ulke, PostaKodu = i.PostaKodu, MapsKonumu = i.MapsKonumu,
        EkAciklama = i.EkAciklama, WebSira = i.WebSira, DropKarsilamaTuru = i.DropKarsilamaTuru,
        DropCalismaSekli = i.DropCalismaSekli, OzelMail = i.OzelMail, OzelTelefon = i.OzelTelefon,
        HaftalikCalismaSaatleri = (i.HaftalikCalismaSaatleri ?? [])
            .Select(h => new GunSaat { Gun = h.Gun, Acilis = h.Acilis, Kapanis = h.Kapanis, Kapali = h.Kapali }).ToList(),
        Aktif = i.Aktif,
    };

    private static async Task<LocationDto?> LocationAsync(Guid id, LocationService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? LocationDto.From(x, version) : null;
    }
}
