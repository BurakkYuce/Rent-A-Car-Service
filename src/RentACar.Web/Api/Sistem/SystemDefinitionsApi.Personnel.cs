using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// Personel (İK kaydı). TÜM uçlar ManageUsers (Blazor + servis paritesi; TC/maaş hassas). KVKK:
/// <list type="bullet">
/// <item>TC kimlik HİÇBİR yanıtta dönmez (liste, detay, oluştur/güncelle yanıtı) — yalnız <c>tcKimlikTanimli</c>.</item>
/// <item>Liste satırı maaş da taşımaz; maaş yalnız tekil detayda (ManageUsers).</item>
/// <item>Operasyon ekranlarının PII'sız seçimi ayrı uçtur (<c>/secim/personel</c>, <c>ListForSelectAsync</c>).</item>
/// </list>
/// </summary>
public static partial class SystemDefinitionsApi
{
    private static readonly (string, string)[] PersonnelRules =
    [
        ("Sicil", "kod"), ("'", "kod"), ("Ad zorunludur", "ad"), ("Soyad zorunludur", "soyad"), ("Maaş", "maas"),
        ("İşten çıkış", "iseCikis"), ("Doğum tarihi", "dogumTarihi"),
    ];

    private static readonly SortFieldMap<PersonnelListItem> PersonnelSort = SortFieldMap<PersonnelListItem>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("soyad", x => x.Soyad)
        .Alan("sube", x => x.Sube).Alan("gorevTanimi", x => x.GorevTanimi).Alan("iseGiris", x => x.IseGiris)
        .Alan("aktif", x => x.Aktif);

    private static void MapPersonnel(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/personel").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.ManageUsers);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? ara, bool? aktif, string? sube, string? gorevTanimi,
            PersonnelService s, CancellationToken ct) =>
        {
            var rows = await s.SearchAsync(new PersonelFilter { Ara = ara, Aktif = aktif, Sube = sube, GorevTanimi = gorevTanimi }, ct);
            var items = rows.Select(p => new PersonnelListItem(p.Id, p.Kod, p.Ad, p.Soyad, p.Sube, p.GorevTanimi, p.CepTel,
                p.MailAdresi, p.IseGiris, p.IseCikis, p.Aktif)).ToList();
            return TypedResults.Ok(F5Ortak.Sayfala(items, PersonnelSort, sayfa, boyut, sirala));
        }).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", async Task<Results<Ok<PersonnelDto>, ProblemHttpResult>> (Guid id, PersonnelService s, CancellationToken ct)
            => await PersonnelAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<PersonnelDto>, ProblemHttpResult>> (PersonnelRequest i, PersonnelService s, CancellationToken ct) =>
        {
            PersonnelLimits(i);
            var id = await s.CreateAsync(PersonnelInput(i), ct);
            return await PersonnelAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/personel/{id}", d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(PersonnelRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<PersonnelDto>, ProblemHttpResult>> (Guid id, PersonnelRequest i, PersonnelService s, CancellationToken ct) =>
        {
            if (await s.GetDetailAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            PersonnelLimits(i);
            if (!await s.UpdateAsync(id, PersonnelInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await PersonnelAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).AlanlariEsle(PersonnelRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, PersonnelService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
    }

    private static void PersonnelLimits(PersonnelRequest i)
    {
        Text(i.Ad, 128, "ad", "Ad");
        Text(i.Soyad, 128, "soyad", "Soyad");
        // TC: 11 hane rakam (düz metin yalnız gövdede; şifreli saklanır, yanıtta dönmez). Yalnız boşluktan oluşan değer
        // belirsizdir (koru mu, sil mi?) → reddedilir; silmek için tam "" gönderilir (#308 M1).
        if (i.TcKimlik is { Length: > 0 } && string.IsNullOrWhiteSpace(i.TcKimlik))
            throw new ValidationException("TC kimlik no yalnız boşluktan oluşamaz; silmek için alanı tamamen boş gönderin.", "tcKimlik");
        if (SystemApiCommon.Clean(i.TcKimlik) is { } tc && (tc.Length != 11 || !tc.All(char.IsAsciiDigit)))
            throw new ValidationException("TC kimlik no 11 haneli rakam olmalıdır.", "tcKimlik");
        Text(i.SurucuBelgeNo, 64, "surucuBelgeNo", "Sürücü belge no");
        Amount(i.Maas, "maas", "Maaş");
        Text(i.Sube, 128, "sube", "Şube");
        Text(i.GorevTanimi, 64, "gorevTanimi", "Görev tanımı");
        Text(i.Adres, 512, "adres", "Adres");
        Text(i.EvTelefonu, 32, "evTelefonu", "Ev telefonu");
        Text(i.IsTelefonu, 32, "isTelefonu", "İş telefonu");
        Text(i.CepTel, 32, "cepTel", "Cep telefonu");
        Text(i.MailAdresi, 128, "mailAdresi", "Mail adresi");
        Text(i.Referans, 256, "referans", "Referans");
        Text(i.Aciklama, 1024, "aciklama", "Açıklama");
        Text(i.SSinifi, 16, "sSinifi", "Ehliyet sınıfı");
        Text(i.SVerilisYeri, 128, "sVerilisYeri", "Ehliyet veriliş yeri");
        Text(i.DogumYeri, 128, "dogumYeri", "Doğum yeri");
        Text(i.BabaAdi, 128, "babaAdi", "Baba adı");
        Text(i.AnaAdi, 128, "anaAdi", "Ana adı");
        Text(i.Il, 64, "il", "İl");
        Text(i.Ilce, 64, "ilce", "İlçe");
        Text(i.Mahalle, 128, "mahalle", "Mahalle");
        Text(i.CiltNo, 32, "ciltNo", "Cilt no");
        Text(i.AileSiraNo, 32, "aileSiraNo", "Aile sıra no");
        Text(i.SiraNo, 32, "siraNo", "Sıra no");
        Text(i.KanGrubu, 8, "kanGrubu", "Kan grubu");
        Text(i.RacTabletNo, 32, "racTabletNo", "Tablet no");
    }

    private static PersonelInput PersonnelInput(PersonnelRequest i) => new()
    {
        // F11.2d yazma-yalnız kuralı: null = koru, "" (boş metin) = sil, dolu = yeni değer.
        // YALNIZ tam boş metin siler; boşluktan oluşan değer (" ", "\t", NBSP) PersonnelLimits'te 400 alır (#308 M1).
        ClearTcKimlik = i.TcKimlik is { Length: 0 }, ClearMaas = i.MaasTemizle,
        Kod = i.Kod, Ad = i.Ad, Soyad = i.Soyad, TcKimlik = i.TcKimlik, IseGiris = Utc(i.IseGiris), IseCikis = Utc(i.IseCikis),
        SurucuBelgeNo = i.SurucuBelgeNo, Maas = i.Maas, Sube = i.Sube, GorevTanimi = i.GorevTanimi, Adres = i.Adres,
        EvTelefonu = i.EvTelefonu, IsTelefonu = i.IsTelefonu, CepTel = i.CepTel, MailAdresi = i.MailAdresi,
        Referans = i.Referans, Aciklama = i.Aciklama, SSinifi = i.SSinifi, SVerilisTarihi = Utc(i.SVerilisTarihi),
        SVerilisYeri = i.SVerilisYeri, DogumTarihi = Utc(i.DogumTarihi), DogumYeri = i.DogumYeri, BabaAdi = i.BabaAdi,
        AnaAdi = i.AnaAdi, Il = i.Il, Ilce = i.Ilce, Mahalle = i.Mahalle, CiltNo = i.CiltNo, AileSiraNo = i.AileSiraNo,
        SiraNo = i.SiraNo, KanGrubu = i.KanGrubu, RacTabletNo = i.RacTabletNo, Aktif = i.Aktif,
    };

    /// <summary>Detay → DTO. TC değeri yalnız "tanımlı mı"ya indirgenir; düz metin DTO'ya hiç girmez.</summary>
    private static async Task<PersonnelDto?> PersonnelAsync(Guid id, PersonnelService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        if (await s.GetDetailAsync(id, ct) is not { Ham: { } r } d) return null;
        return new PersonnelDto(
            r.Id, r.Kod, r.Ad, r.Soyad, !string.IsNullOrWhiteSpace(d.TcKimlik), r.IseGiris, r.IseCikis, r.SurucuBelgeNo,
            d.Maas, r.Sube, r.SubeId, r.GorevTanimi, r.Adres, r.EvTelefonu, r.IsTelefonu, r.CepTel, r.MailAdresi,
            r.Referans, r.Aciklama, r.SSinifi, r.SVerilisTarihi, r.SVerilisYeri, r.DogumTarihi, r.DogumYeri, r.BabaAdi,
            r.AnaAdi, r.Il, r.Ilce, r.Mahalle, r.CiltNo, r.AileSiraNo, r.SiraNo, r.KanGrubu, r.RacTabletNo, r.Aktif,
            version);
    }
}
