using RentACar.Application.Authorization;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Application.CustomCodes;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Kur;
using RentACar.Application.Locations;
using RentACar.Application.Personnel;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Secim;

/// <summary>Genel seçim satırı: kimlik + görünen etiket (+ varsa kod). PII TAŞIMAZ.</summary>
public sealed record SecimOgesi(Guid Id, string Etiket, string? Kod);

/// <summary>Müşteri seçimi — yalnız kimlik, görünen ad ve tip (Bireysel/Kurumsal/Servis). TC/telefon/e-posta/adres YOK.</summary>
public sealed record MusteriSecimOgesi(Guid Id, string Etiket, string Tip);

/// <summary>Araç seçimi — formun ihtiyacı: plaka, grup, durum (hepsi operasyonel, kişisel veri değil).</summary>
public sealed record AracSecimOgesi(Guid Id, string Etiket, string Plaka, string? Grup, string Durum);

/// <summary>Lokasyon seçimi — <c>SubeId</c>: SPA çıkış ofisini operatörün şubesine göre süzebilsin (asıl kapı sunucuda).</summary>
public sealed record LokasyonSecimOgesi(Guid Id, string Etiket, string Kod, Guid? SubeId);

/// <summary>Kur seçimi — TCMB ham değeri (<c>Birim</c> kadar döviz için); ulusal veri, kiracıya ait değil. Id = ISO kod.</summary>
public sealed record KurSecimOgesi(string Id, string Etiket, int Birim, decimal? DovizAlis, decimal? DovizSatis, DateTimeOffset Tarih);

/// <summary>
/// F1.6 — yeni arayüzün F4–F5 formları için typeahead/seçim kaynakları. Tek sözleşme:
/// <list type="bullet">
/// <item><b>Sınırlı:</b> <c>limit</c> varsayılan ve en çok <see cref="AzamiLimit"/> (20); geçersiz değer 20'ye düşer.</item>
/// <item><b>Türkçe katlamalı arama</b> (<see cref="TurkishText.Normalize"/>): "ışık" = "IŞIK" = "isik".
/// Önce etiketi sorgu ile BAŞLAYANLAR, sonra içerenler; kendi içinde katlanmış etikete göre (kültürden
/// bağımsız, deterministik).</item>
/// <item><b>Yetkili:</b> <see cref="Permission.OperationsWrite"/> — uç kapısına EK servis guard'ı (çift savunma).
/// Altta yatan <c>ListActiveAsync</c>'lerin çoğu "yetki gerektirmez" (Blazor formları için); yeni yüzey
/// bu gevşekliği TAŞIMAZ.</item>
/// <item><b>PII yok:</b> yalnız kimlik + etiket + formun ihtiyacı olan operasyonel alanlar.</item>
/// <item><b>Şube kapsamı:</b> araç (<see cref="VehicleService.ListAsync"/> kapsamı), personel ve şube
/// operatörün şubesine daralır (<see cref="BranchScope.InScope"/> — TEK kural). Müşteri kiracı geneli (şube
/// alanı yok); lokasyon bilinçli kapsamsız (dönüş ofisi başka şube olabilir; çıkış ofisi kapsamı
/// <c>RentalService</c>'te sunucuda zorlanır).</item>
/// </list>
/// </summary>
public sealed class SecimService(
    ICurrentUser currentUser,
    CustomerService musteriler,
    VehicleService araclar,
    LocationService lokasyonlar,
    EkHizmetTanimService ekHizmetler,
    CoverageProductService sigortalar,
    ReservationSourceService kaynaklar,
    CustomCodeService ozelKodlar,
    BelgeSablonService sablonlar,
    PersonelService personeller,
    KurService kurlar,
    BranchService subeler,
    VehicleGroupService aracGruplari)
{
    public const int AzamiLimit = 20;
    private const int AzamiSorgu = 100;

    /// <summary><c>limit</c> normalizasyonu: 1..20 aynen; yoksa/geçersizse/büyükse 20.</summary>
    public static int Sinir(int? limit) => limit is > 0 and <= AzamiLimit ? limit.Value : AzamiLimit;

    private void Kapi() => PermissionGuard.Require(currentUser, Permission.OperationsWrite);

    public async Task<IReadOnlyList<MusteriSecimOgesi>> MusteriAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        var satirlar = await musteriler.SecimAraAsync(q, Sinir(limit), ct);
        return satirlar.Select(s => new MusteriSecimOgesi(s.Id, s.Ad, s.Tip.ToString())).ToList();
    }

    public async Task<IReadOnlyList<AracSecimOgesi>> AracAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        var liste = await araclar.ListAsync(ct); // şube kapsamı VehicleService'te (operatör yalnız kendi şubesi)
        return Suz(liste, q, limit, v => AracEtiketi(v.Plaka, v.Marka, v.Tip), v => v.Plaka, v => v.Grup)
            .Select(v => new AracSecimOgesi(v.Id, AracEtiketi(v.Plaka, v.Marka, v.Tip), v.Plaka, v.Grup, v.Durum.ToString()))
            .ToList();
    }

    /// <summary>
    /// F4.3b — kimlikle TEK müşteri etiketi (bağlantıdaki <c>?musteriId=</c>). Arama ucuyla AYNI izin ve AYNI
    /// PII kuralı (yalnız kimlik + görünen ad + tip; repo PII kolonuna dokunmaz). Yok/başka kiracı → <c>null</c> (404).
    /// Müşteri kiracı genelidir (şube alanı yok) — arama ucundaki kapsamla aynı.
    /// </summary>
    public async Task<MusteriSecimOgesi?> MusteriGetirAsync(Guid id, CancellationToken ct = default)
    {
        Kapi();
        var s = await musteriler.SecimGetirAsync(id, ct);
        return s is null ? null : new MusteriSecimOgesi(s.Id, s.Ad, s.Tip.ToString());
    }

    /// <summary>
    /// F4.3b — kimlikle TEK araç etiketi (bağlantıdaki <c>?varac=</c>). <see cref="VehicleService.GetAsync"/> şube
    /// kapsamını zorlar: kapsam dışı → <see cref="YetkiYokException"/> (403); yok/başka kiracı → <c>null</c> (404).
    /// Alan kümesi arama ucuyla aynı (plaka, grup, durum — kişisel veri yok).
    /// </summary>
    public async Task<AracSecimOgesi?> AracGetirAsync(Guid id, CancellationToken ct = default)
    {
        Kapi();
        var v = await araclar.GetAsync(id, ct);
        return v is null ? null
            : new AracSecimOgesi(v.Id, AracEtiketi(v.Plaka, v.Marka, v.Tip), v.Plaka, v.Grup, v.Durum.ToString());
    }

    public async Task<IReadOnlyList<LokasyonSecimOgesi>> LokasyonAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await lokasyonlar.ListActiveAsync(ct), q, limit, l => l.Ad, l => l.Kod)
            .Select(l => new LokasyonSecimOgesi(l.Id, l.Ad, l.Kod, l.SubeId)).ToList();
    }

    public async Task<IReadOnlyList<SecimOgesi>> EkHizmetAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await ekHizmetler.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    public async Task<IReadOnlyList<SecimOgesi>> SigortaUrunuAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await sigortalar.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    /// <summary>Rezervasyon kaynağı (<see cref="MasterTanimService{T}"/> tanımı; Blazor formu değer olarak <c>Ad</c> yazar).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> RezervasyonKaynagiAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Tanim(await kaynaklar.ListActiveAsync(ct), q, limit);
    }

    public async Task<IReadOnlyList<SecimOgesi>> OzelKodAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await ozelKodlar.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    /// <summary>Belge şablonu — yalnız AKTİF; <paramref name="tur"/> verilmezse tüm türler.</summary>
    public async Task<IReadOnlyList<SecimOgesi>> BelgeSablonuAsync(string? q, int? limit, BelgeTuru? tur, CancellationToken ct = default)
    {
        Kapi();
        var liste = tur is { } t ? await sablonlar.ListByTuruAsync(t, ct) : await sablonlar.ListAsync(ct);
        return Suz(liste.Where(s => s.Aktif), q, limit, s => s.Ad)
            .Select(s => new SecimOgesi(s.Id, s.Ad, null)).ToList();
    }

    /// <summary>Personel — PII'sız seçim listesi (<see cref="PersonelService.ListForSelectAsync"/>) + şube kapsamı.</summary>
    public async Task<IReadOnlyList<SecimOgesi>> PersonelAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        var kapsam = BranchScope.EffectiveFilter(currentUser);
        var liste = (await personeller.ListForSelectAsync(ct))
            .Where(p => BranchScope.InScope(kapsam, p.SubeId, p.Sube));
        return Suz(liste, q, limit, p => PersonelEtiketi(p.Ad, p.Soyad))
            .Select(p => new SecimOgesi(p.Id, PersonelEtiketi(p.Ad, p.Soyad), null)).ToList();
    }

    /// <summary>Günün TCMB kurları (en yeni gün). Ulusal veri; kiracıya/kişiye ait değil.</summary>
    public async Task<IReadOnlyList<KurSecimOgesi>> KurAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await kurlar.BugunKurlarAsync(ct), q, limit, k => $"{k.Kod} — {k.Ad}")
            .Select(k => new KurSecimOgesi(k.Kod, $"{k.Kod} — {k.Ad}", k.Birim, k.ForexAlis, k.ForexSatis, k.Tarih))
            .ToList();
    }

    /// <summary>Şube — operatör yalnız kendi şubesini görür (FK öncelikli, metin yedek; <see cref="BranchScope.InScope"/>).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> SubeAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        var kapsam = BranchScope.EffectiveFilter(currentUser);
        var liste = (await subeler.ListActiveAsync(ct)).Where(b => BranchScope.InScope(kapsam, b.Id, b.Ad));
        return Suz(liste, q, limit, b => b.Ad, b => b.Kod)
            .Select(b => new SecimOgesi(b.Id, b.Ad, b.Kod)).ToList();
    }

    /// <summary>Araç grubu (F5 müsaitlik/rezervasyon grup seçimi).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> AracGrubuAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Kapi();
        return Suz(await aracGruplari.ListActiveAsync(ct), q, limit, g => g.Ad, g => g.Kod)
            .Select(g => new SecimOgesi(g.Id, g.Ad, g.Kod)).ToList();
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary><see cref="MasterTanimService{T}"/> tanımları için ortak eşleme (Kod + Ad).</summary>
    private static IReadOnlyList<SecimOgesi> Tanim<T>(IEnumerable<T> liste, string? q, int? limit) where T : IMasterTanim
        => Suz(liste, q, limit, x => x.Ad, x => x.Kod).Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();

    private static string AracEtiketi(string plaka, string? marka, string? tip)
    {
        var ek = $"{marka} {tip}".Trim();
        return ek.Length == 0 ? plaka : $"{plaka} — {ek}";
    }

    private static string PersonelEtiketi(string ad, string soyad) => $"{ad} {soyad}".Trim();

    /// <summary>
    /// Bellek-içi süzme (önbellekli master listeleri): Türkçe katlamalı "içerir"; etiketi terimle
    /// BAŞLAYANLAR önce, sonra katlanmış etiket (ordinal — OS yerelinden bağımsız); en çok <see cref="Sinir"/> kadar.
    /// </summary>
    private static List<T> Suz<T>(IEnumerable<T> kaynak, string? q, int? limit, Func<T, string> etiket,
        params Func<T, string?>[] ekAlanlar)
    {
        var terim = q?.Trim() ?? string.Empty;
        if (terim.Length > AzamiSorgu) terim = terim[..AzamiSorgu];
        terim = TurkishText.Normalize(terim);

        var adaylar = kaynak.Select(x => (Oge: x, Etiket: etiket(x), Katli: TurkishText.Normalize(etiket(x))));
        if (terim.Length > 0)
            adaylar = adaylar.Where(a => a.Katli.Contains(terim, StringComparison.Ordinal)
                                        || ekAlanlar.Any(f => f(a.Oge) is { } v && TurkishText.Normalize(v).Contains(terim, StringComparison.Ordinal)));
        return adaylar
            .OrderBy(a => terim.Length > 0 && a.Katli.StartsWith(terim, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(a => a.Katli, StringComparer.Ordinal)
            .ThenBy(a => a.Etiket, StringComparer.Ordinal)
            .Take(Sinir(limit))
            .Select(a => a.Oge)
            .ToList();
    }
}
