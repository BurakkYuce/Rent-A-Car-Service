using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.RezSartlar;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// <c>/api/ui/v1/rez-sartlari/*</c> — rez şartı (müşteri özel talebi) JSON uçları (F5.1). İş mantığı
/// <see cref="RezSartService"/>'te. İzin: OperationsWrite (menü kaydı + Blazor grubu). Para yok, defter yok.
/// <para><b>Üst kayıt kapsamı:</b> şart bir rezervasyona/teklife bağlıysa (<c>reservationId</c>/<c>quotationId</c>,
/// gevşek bağ — FK yok) o kaydın şube kapsamından geçer: kimlikli uçlarda ÖNCE bağlı kaydın kapsamı (başka şube →
/// 403), sonra işlem; listede kapsam dışı kayda bağlı şartlar görünmez; oluştur/güncellemede bağlanan kayıt kapsamda
/// ve bu kiracıda olmalı. Bağsız şart kiracı genelidir (Blazor ile aynı; müşterinin şube alanı yok).</para>
/// <para>PUT tam değiştirmedir: zorunlu <c>surum</c>, uyuşmazlık 409 <c>cakisma</c>.</para>
/// </summary>
public static class RezSartApi
{
    private const string Kok = UiApiExtensions.V1 + "/rez-sartlari";

    public static RouteGroupBuilder MapRezSartApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/rez-sartlari")
            .WithTags("Rez Şartı")
            .RequirePermission(Permission.OperationsWrite);

        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/gruplar", Gruplar);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur).AlanlariEsle(YazmaKurallari);
        g.MapPut("/{id:guid}", Guncelle).AlanlariEsle(YazmaKurallari);
        g.MapPost("/{id:guid}/karsilandi", Karsilandi);
        g.MapPost("/{id:guid}/geri-al", GeriAl);
        g.MapDelete("/{id:guid}", Sil);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Rez şartı bulunamadı.");

    // ================================================================== kapsam

    /// <summary>
    /// Bağlı üst kaydın kapsamı. Rezervasyon/teklif servislerinin <c>GetAsync</c>'i kapsam dışında
    /// <see cref="YetkiYokException"/> atar (403). Bağlı kayıt artık yoksa (gevşek bağ) kapı açıktır — kayıt silinmez,
    /// yalnız iptal edilir; kaybolmuş bağ başka şubenin verisini sızdırmaz.
    /// </summary>
    private static async Task UstKayitKapsamiAsync(
        RezSart s, ReservationService rezervasyonlar, QuotationService teklifler, CancellationToken ct)
    {
        if (s.ReservationId is Guid r) await rezervasyonlar.GetAsync(r, ct);
        if (s.QuotationId is Guid t) await teklifler.GetAsync(t, ct);
    }

    /// <summary>Kimlikli uçların ortak kapısı: yok/başka kiracı → null (404); bağlı kayıt kapsam dışı → 403.</summary>
    private static async Task<RezSart?> KapsamliAsync(
        Guid id, RezSartService sartlar, ReservationService rezervasyonlar, QuotationService teklifler, CancellationToken ct)
    {
        var s = await sartlar.GetAsync(id, ct);
        if (s is not null) await UstKayitKapsamiAsync(s, rezervasyonlar, teklifler, ct);
        return s;
    }

    // ================================================================== okumalar

    private static readonly SiralamaHaritasi<RezSartDto> Harita = SiralamaHaritasi<RezSartDto>
        .Olustur(s => s.Id)
        .Alan("talepTarihi", s => s.TalepTarihi)
        .Alan("musteri", s => s.MusteriAd)
        .Alan("grup", s => s.Grup)
        .Alan("sart", s => s.Sart)
        .Alan("karsilamaTarihi", s => s.KarsilamaTarihi);

    /// <summary>Blazor RezSartList süzgeçleri: müşteri, durum (<c>bekleyen</c>|<c>karsilanan</c>), talep günü aralığı.</summary>
    public sealed class RezSartListeFiltresi
    {
        [FromQuery(Name = "musteriId")] public Guid? MusteriId { get; set; }
        /// <summary><c>bekleyen</c> (karşılanmadı) | <c>karsilanan</c> | boş (hepsi).</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "bas")] public DateOnly? Bas { get; set; }
        [FromQuery(Name = "bit")] public DateOnly? Bit { get; set; }

        public RezSartFilter ToFilter()
        {
            var (min, max) = F5Ortak.GunAraligi(Bas, Bit);
            return new RezSartFilter
            {
                MusteriId = MusteriId,
                Karsilandi = F5Ortak.Nz(Durum)?.ToLowerInvariant() switch
                {
                    null => null,
                    "bekleyen" => false,
                    "karsilanan" => true,
                    _ => throw new ValidationException("Geçersiz durum değeri. İzin verilenler: bekleyen, karsilanan.", "durum"),
                },
                TarihBas = min,
                TarihBit = max,
            };
        }
    }

    private static async Task<Ok<Sayfa<RezSartDto>>> Liste(
        [AsParameters] RezSartListeFiltresi f, RezSartService sartlar, ReservationService rezervasyonlar,
        QuotationService teklifler, ICurrentUser kullanici, IDbContextFactory<AppDbContext> dbf,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        IEnumerable<RezSart> liste = await sartlar.ListAsync(f.ToFilter(), ct);
        if (!BranchScope.EffectiveFilter(kullanici).Unrestricted)
        {
            // Kapsamlı kullanıcı: bağlı kaydı kapsamında OLMAYAN şartlar düşer (tekil uçların 403'üyle tutarlı).
            // Bağlı kaydın hiç olmadığı (kaybolmuş) bağ tekil kapıda açık → burada da görünür.
            var tumRez = liste.Where(s => s.ReservationId is not null).Select(s => s.ReservationId!.Value).ToHashSet();
            var tumTek = liste.Where(s => s.QuotationId is not null).Select(s => s.QuotationId!.Value).ToHashSet();
            var disRez = await KapsamDisiAsync(dbf, tumRez, (await rezervasyonlar.ListAsync(ct)).Select(r => r.Id), rez: true, ct);
            var disTek = await KapsamDisiAsync(dbf, tumTek, (await teklifler.ListAsync(ct)).Select(t => t.Id), rez: false, ct);
            liste = liste.Where(s => !(s.ReservationId is Guid r && disRez.Contains(r)) && !(s.QuotationId is Guid t && disTek.Contains(t)));
        }
        var satirlar = liste.ToList();
        var cariler = await F5Ortak.CarilerAsync(dbf, satirlar.Select(s => s.MusteriId), ct);
        var dtolar = satirlar.Select(s => RezSartDto.From(s, F5Ortak.CariAdi(cariler, s.MusteriId), null)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(dtolar, Harita, sayfa, boyut, sirala));
    }

    /// <summary>Bağlı kimliklerden VAR olup kapsamda OLMAYANLAR (kapsamlı liste servisten; varlık RLS kapsamlı okumayla).</summary>
    private static async Task<HashSet<Guid>> KapsamDisiAsync(
        IDbContextFactory<AppDbContext> dbf, HashSet<Guid> bagli, IEnumerable<Guid> kapsamli, bool rez, CancellationToken ct)
    {
        if (bagli.Count == 0) return [];
        var ids = bagli.ToList();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var mevcut = rez
            ? await db.Reservations.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct)
            : await db.Quotations.AsNoTracking().Where(q => ids.Contains(q.Id)).Select(q => q.Id).ToListAsync(ct);
        var gorunur = kapsamli.ToHashSet();
        return mevcut.Where(i => !gorunur.Contains(i)).ToHashSet();
    }

    /// <summary>Grup önerileri (serbest metin; mevcut şartlarda geçen gruplar — Blazor datalist'iyle aynı kaynak).</summary>
    private static async Task<Ok<IReadOnlyList<string>>> Gruplar(RezSartService sartlar, CancellationToken ct)
        => TypedResults.Ok<IReadOnlyList<string>>((await sartlar.ListAsync(null, ct))
            .Select(r => r.Grup).Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g!)
            .Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.Ordinal).ToList());

    private static async Task<Results<Ok<RezSartDto>, ProblemHttpResult>> Detay(
        Guid id, RezSartService sartlar, IRezSartRepository depo, ReservationService rezervasyonlar, QuotationService teklifler,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DtoAsync(id, sartlar, depo, rezervasyonlar, teklifler, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    /// <summary>Sürüm alanlardan ÖNCE okunur (bkz. kira detay).</summary>
    private static async Task<RezSartDto?> DtoAsync(
        Guid id, RezSartService sartlar, IRezSartRepository depo, ReservationService rezervasyonlar, QuotationService teklifler,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var surum = await depo.SurumAsync(id, ct);
        var s = await KapsamliAsync(id, sartlar, rezervasyonlar, teklifler, ct);
        if (s is null) return null;
        var cariler = await F5Ortak.CarilerAsync(dbf, [s.MusteriId], ct);
        return RezSartDto.From(s, F5Ortak.CariAdi(cariler, s.MusteriId), surum);
    }

    // ================================================================== yazmalar

    private static readonly (string, string)[] YazmaKurallari =
    [
        ("Müşteri seçilmelidir", "musteriId"),
        ("Seçilen müşteri bulunamadı", "musteriId"),
        ("Şart/talep metni zorunludur", "sart"),
        ("Şart metni en çok", "sart"),
        ("Geçerlilik bitişi başlangıçtan önce", "bitTar"),
        ("Talep tarihi gelecekte", "talepTarihi"),
        ("Karşılama tarihi gelecekte", "karsilamaTarihi"),
    ];

    /// <summary>Uç sınırları (varchar) + bağlanan üst kaydın varlığı ve kapsamı.</summary>
    private static async Task<RezSartInput> GirdiAsync(
        RezSartIstegi i, ReservationService rezervasyonlar, QuotationService teklifler, CancellationToken ct)
    {
        Sinirlar.Metin(i.Sart, 512, "sart", "Şart metni");
        Sinirlar.Metin(i.Grup, 64, "grup", "Grup");
        Sinirlar.Metin(i.TeslimEden, 128, "teslimEden", "Teslim eden");
        if (i.ReservationId is Guid r && await rezervasyonlar.GetAsync(r, ct) is null) // kapsam dışı → 403
            throw new ValidationException("Bağlanan rezervasyon bulunamadı.", "reservationId");
        if (i.QuotationId is Guid t && await teklifler.GetAsync(t, ct) is null)
            throw new ValidationException("Bağlanan teklif bulunamadı.", "quotationId");
        return new RezSartInput
        {
            MusteriId = i.MusteriId,
            Sart = i.Sart ?? "",
            Grup = F5Ortak.Nz(i.Grup),
            BasTar = F5Ortak.Utc(i.BasTar),
            BitTar = F5Ortak.Utc(i.BitTar),
            TalepTarihi = F5Ortak.Utc(i.TalepTarihi),
            KarsilamaTarihi = F5Ortak.Utc(i.KarsilamaTarihi),
            TeslimEden = F5Ortak.Nz(i.TeslimEden),
            ReservationId = i.ReservationId,
            QuotationId = i.QuotationId,
        };
    }

    private static async Task<Created<RezSartDto>> Olustur(
        RezSartIstegi istek, RezSartService sartlar, IRezSartRepository depo, ReservationService rezervasyonlar,
        QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var id = await sartlar.CreateAsync(await GirdiAsync(istek, rezervasyonlar, teklifler, ct), ct);
        var dto = await DtoAsync(id, sartlar, depo, rezervasyonlar, teklifler, dbf, ct);
        return TypedResults.Created($"{Kok}/{id}", dto!);
    }

    private static async Task<Results<Ok<RezSartDto>, ProblemHttpResult>> Guncelle(
        Guid id, RezSartGuncelleIstegi istek, RezSartService sartlar, IRezSartRepository depo,
        ReservationService rezervasyonlar, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await KapsamliAsync(id, sartlar, rezervasyonlar, teklifler, ct) is null) return Bulunamadi();
        if (string.IsNullOrWhiteSpace(istek.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        var girdi = await GirdiAsync(istek, rezervasyonlar, teklifler, ct);
        if (!await sartlar.UpdateAsync(id, girdi, istek.Surum, ct)) return Bulunamadi();
        return await DtoAsync(id, sartlar, depo, rezervasyonlar, teklifler, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    /// <summary>Karşılandı işaretle. Zaten karşılanmışsa tarih DEĞİŞMEZ (servis kuralı) → çift tık zararsız.</summary>
    private static async Task<Results<Ok<RezSartDto>, ProblemHttpResult>> Karsilandi(
        Guid id, RezSartKarsilandiIstegi? istek, RezSartService sartlar, IRezSartRepository depo,
        ReservationService rezervasyonlar, QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await KapsamliAsync(id, sartlar, rezervasyonlar, teklifler, ct) is null) return Bulunamadi();
        Sinirlar.Metin(istek?.TeslimEden, 128, "teslimEden", "Teslim eden");
        if (!await sartlar.KarsilandiIsaretleAsync(id, F5Ortak.Nz(istek?.TeslimEden), ct)) return Bulunamadi();
        return await DtoAsync(id, sartlar, depo, rezervasyonlar, teklifler, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<RezSartDto>, ProblemHttpResult>> GeriAl(
        Guid id, RezSartService sartlar, IRezSartRepository depo, ReservationService rezervasyonlar,
        QuotationService teklifler, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await KapsamliAsync(id, sartlar, rezervasyonlar, teklifler, ct) is null) return Bulunamadi();
        if (!await sartlar.KarsilamaGeriAlAsync(id, ct)) return Bulunamadi();
        return await DtoAsync(id, sartlar, depo, rezervasyonlar, teklifler, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    /// <summary>Silme (Blazor ile aynı izin: OperationsWrite — operasyonel not, mali belge değil).</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Sil(
        Guid id, RezSartService sartlar, ReservationService rezervasyonlar, QuotationService teklifler, CancellationToken ct)
    {
        if (await KapsamliAsync(id, sartlar, rezervasyonlar, teklifler, ct) is null) return Bulunamadi();
        return await sartlar.DeleteAsync(id, ct) ? TypedResults.NoContent() : Bulunamadi();
    }
}

/// <summary>Rez şartı alanları (<c>POST /rez-sartlari</c>). PUT tam değiştirmedir (bağ kimlikleri dahil geri gönderilir).</summary>
public record RezSartIstegi
{
    public required Guid MusteriId { get; init; }
    public string? Sart { get; init; }
    public string? Grup { get; init; }
    public DateTimeOffset? BasTar { get; init; }
    public DateTimeOffset? BitTar { get; init; }
    /// <summary>Boş → oluştururken şimdi; güncellemede MEVCUT değer korunur.</summary>
    public DateTimeOffset? TalepTarihi { get; init; }
    public DateTimeOffset? KarsilamaTarihi { get; init; }
    public string? TeslimEden { get; init; }
    public Guid? ReservationId { get; init; }
    public Guid? QuotationId { get; init; }
}

/// <summary><c>PUT /rez-sartlari/{id}</c> — <c>surum</c> ZORUNLU.</summary>
public sealed record RezSartGuncelleIstegi : RezSartIstegi
{
    public string? Surum { get; init; }
}

public sealed record RezSartKarsilandiIstegi(string? TeslimEden);

/// <summary>Rez şartı. <c>Surum</c> yalnız tekil yanıtlarda dolu (listede null). <c>Karsilandi</c> = <c>KarsilamaTarihi</c> dolu.</summary>
public sealed record RezSartDto(
    Guid Id, Guid MusteriId, string MusteriAd, string Sart, string? Grup, DateTimeOffset? BasTar, DateTimeOffset? BitTar,
    DateTimeOffset TalepTarihi, DateTimeOffset? KarsilamaTarihi, bool Karsilandi, string? TeslimEden,
    Guid? ReservationId, Guid? QuotationId, string? Surum)
{
    public static RezSartDto From(RezSart s, string musteriAd, string? surum) => new(
        s.Id, s.MusteriId, musteriAd, s.Sart, s.Grup, s.BasTar, s.BitTar, s.TalepTarihi, s.KarsilamaTarihi,
        s.KarsilamaTarihi is not null, s.TeslimEden, s.ReservationId, s.QuotationId, surum);
}
