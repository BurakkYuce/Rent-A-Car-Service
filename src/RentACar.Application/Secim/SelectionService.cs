using RentACar.Application.Authorization;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Application.CustomCodes;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.ExpenseCategories;
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
/// <item><b>Sınırlı:</b> <c>limit</c> varsayılan ve en çok <see cref="MaxLimit"/> (20); geçersiz değer 20'ye düşer.</item>
/// <item><b>Türkçe katlamalı arama</b> (<see cref="TurkishText.Normalize"/>): "ışık" = "IŞIK" = "isik".
/// Önce etiketi sorgu ile BAŞLAYANLAR, sonra içerenler; kendi içinde katlanmış etikete göre (kültürden
/// bağımsız, deterministik).</item>
/// <item><b>Yetkili:</b> <see cref="Permission.OperationsWrite"/> — uç kapısına EK servis guard'ı (çift savunma).
/// İstisna (F4.4): müşteri ve kur OperationsWrite VEYA FinanceWrite (sabit finans paneli; PII'sız alanlar).
/// Altta yatan <c>ListActiveAsync</c>'lerin çoğu "yetki gerektirmez" (Blazor formları için); yeni yüzey
/// bu gevşekliği TAŞIMAZ.</item>
/// <item><b>PII yok:</b> yalnız kimlik + etiket + formun ihtiyacı olan operasyonel alanlar.</item>
/// <item><b>Şube kapsamı:</b> araç (<see cref="VehicleService.ListAsync"/> kapsamı), personel ve şube
/// operatörün şubesine daralır (<see cref="BranchScope.InScope"/> — TEK kural). Müşteri kiracı geneli (şube
/// alanı yok); lokasyon bilinçli kapsamsız (dönüş ofisi başka şube olabilir; çıkış ofisi kapsamı
/// <c>RentalService</c>'te sunucuda zorlanır).</item>
/// </list>
/// </summary>
public sealed class SelectionService(
    ICurrentUser currentUser,
    CustomerService customers,
    VehicleService vehicles,
    LocationService locations,
    AddOnDefinitionService addOns,
    CoverageProductService insurances,
    ReservationSourceService sources,
    CustomCodeService customCodes,
    DocumentTemplateService templates,
    PersonnelService staff,
    ExchangeRateService exchangeRates,
    BranchService branches,
    VehicleGroupService vehicleGroups,
    ExpenseCategoryService expenseCategories)
{
    public const int MaxLimit = 20;
    private const int MaxQuery = 100;

    /// <summary><c>limit</c> normalizasyonu: 1..20 aynen; yoksa/geçersizse/büyükse 20.</summary>
    public static int Limit(int? limit) => limit is > 0 and <= MaxLimit ? limit.Value : MaxLimit;

    private void Gate() => PermissionGuard.Require(currentUser, Permission.OperationsWrite);

    /// <summary>F4.4: müşteri ve kur seçimi Muhasebe'ye de açık (kira formunun sabit finans paneli: dış hizmet
    /// tedarikçi carisi, kur bilgisi). Dönen alanlar PII'sız; uç kapısı da "herhangi biri".</summary>
    private void FinanceIncludedGate()
        => PermissionGuard.RequireAny(currentUser, Permission.OperationsWrite, Permission.FinanceWrite);

    public async Task<IReadOnlyList<MusteriSecimOgesi>> CustomerAsync(string? q, int? limit, CancellationToken ct = default)
    {
        FinanceIncludedGate();
        var rows = await customers.SearchSelectionAsync(q, Limit(limit), ct);
        // KVKK: AnonimAd → etiket (CariAnonimlik — Web MusteriGorunumu ile aynı sabit); repo aramada da etiketi eşler.
        return rows.Select(s => new MusteriSecimOgesi(s.Id, CustomerAnonymity.Name(s.Ad, s.AnonimAd), s.Tip.ToString())).ToList();
    }

    public async Task<IReadOnlyList<AracSecimOgesi>> VehicleAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        var list = await vehicles.ListAsync(ct); // şube kapsamı VehicleService'te (operatör yalnız kendi şubesi)
        return Filter(list, q, limit, v => VehicleLabel(v.Plaka, v.Marka, v.Tip), v => v.Plaka, v => v.Grup)
            .Select(v => new AracSecimOgesi(v.Id, VehicleLabel(v.Plaka, v.Marka, v.Tip), v.Plaka, v.Grup, v.Durum.ToString()))
            .ToList();
    }

    /// <summary>
    /// F4.3b — kimlikle TEK müşteri etiketi (bağlantıdaki <c>?musteriId=</c>). Arama ucuyla AYNI izin ve AYNI
    /// PII kuralı (yalnız kimlik + görünen ad + tip; repo PII kolonuna dokunmaz). Yok/başka kiracı → <c>null</c> (404).
    /// Müşteri kiracı genelidir (şube alanı yok) — arama ucundaki kapsamla aynı.
    /// </summary>
    public async Task<MusteriSecimOgesi?> GetCustomerAsync(Guid id, CancellationToken ct = default)
    {
        Gate();
        var s = await customers.GetSelectionAsync(id, ct);
        return s is null ? null : new MusteriSecimOgesi(s.Id, CustomerAnonymity.Name(s.Ad, s.AnonimAd), s.Tip.ToString());
    }

    /// <summary>
    /// F4.3b — kimlikle TEK araç etiketi (bağlantıdaki <c>?varac=</c>). <see cref="VehicleService.GetAsync"/> şube
    /// kapsamını zorlar: kapsam dışı → <see cref="NoPermissionException"/> (403); yok/başka kiracı → <c>null</c> (404).
    /// Alan kümesi arama ucuyla aynı (plaka, grup, durum — kişisel veri yok).
    /// </summary>
    public async Task<AracSecimOgesi?> GetVehicleAsync(Guid id, CancellationToken ct = default)
    {
        Gate();
        var v = await vehicles.GetAsync(id, ct);
        return v is null ? null
            : new AracSecimOgesi(v.Id, VehicleLabel(v.Plaka, v.Marka, v.Tip), v.Plaka, v.Grup, v.Durum.ToString());
    }

    public async Task<IReadOnlyList<LokasyonSecimOgesi>> LocationAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Filter(await locations.ListActiveAsync(ct), q, limit, l => l.Ad, l => l.Kod)
            .Select(l => new LokasyonSecimOgesi(l.Id, l.Ad, l.Kod, l.SubeId)).ToList();
    }

    public async Task<IReadOnlyList<SecimOgesi>> AddOnAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Filter(await addOns.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    public async Task<IReadOnlyList<SecimOgesi>> InsuranceProductAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Filter(await insurances.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    /// <summary>Rezervasyon kaynağı (<see cref="MasterDefinitionService{T}"/> tanımı; Blazor formu değer olarak <c>Ad</c> yazar).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> ReservationSourceAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Definition(await sources.ListActiveAsync(ct), q, limit);
    }

    public async Task<IReadOnlyList<SecimOgesi>> CustomCodeAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Filter(await customCodes.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    /// <summary>Belge şablonu — yalnız AKTİF; <paramref name="type"/> verilmezse tüm türler.</summary>
    public async Task<IReadOnlyList<SecimOgesi>> DocumentTemplateAsync(string? q, int? limit, BelgeTuru? type, CancellationToken ct = default)
    {
        Gate();
        var list = type is { } t ? await templates.ListByTypeAsync(t, ct) : await templates.ListAsync(ct);
        return Filter(list.Where(s => s.Aktif), q, limit, s => s.Ad)
            .Select(s => new SecimOgesi(s.Id, s.Ad, null)).ToList();
    }

    /// <summary>Personel — PII'sız seçim listesi (<see cref="PersonnelService.ListForSelectAsync"/>) + şube kapsamı.</summary>
    public async Task<IReadOnlyList<SecimOgesi>> StaffAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        var scope = BranchScope.EffectiveFilter(currentUser);
        var list = (await staff.ListForSelectAsync(ct))
            .Where(p => BranchScope.InScope(scope, p.SubeId, p.Sube));
        return Filter(list, q, limit, p => StaffLabel(p.Ad, p.Soyad))
            .Select(p => new SecimOgesi(p.Id, StaffLabel(p.Ad, p.Soyad), null)).ToList();
    }

    /// <summary>Günün TCMB kurları (en yeni gün). Ulusal veri; kiracıya/kişiye ait değil.</summary>
    public async Task<IReadOnlyList<KurSecimOgesi>> ExchangeRateAsync(string? q, int? limit, CancellationToken ct = default)
    {
        FinanceIncludedGate();
        return Filter(await exchangeRates.TodayRatesAsync(ct), q, limit, k => $"{k.Kod} — {k.Ad}")
            .Select(k => new KurSecimOgesi(k.Kod, $"{k.Kod} — {k.Ad}", k.Birim, k.ForexAlis, k.ForexSatis, k.Tarih))
            .ToList();
    }

    /// <summary>Şube — operatör yalnız kendi şubesini görür (FK öncelikli, metin yedek; <see cref="BranchScope.InScope"/>).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> BranchAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        var scope = BranchScope.EffectiveFilter(currentUser);
        var list = (await branches.ListActiveAsync(ct)).Where(b => BranchScope.InScope(scope, b.Id, b.Ad));
        return Filter(list, q, limit, b => b.Ad, b => b.Kod)
            .Select(b => new SecimOgesi(b.Id, b.Ad, b.Kod)).ToList();
    }

    /// <summary>
    /// Gider kategorisi (gider türü tanımı) — yalnız AKTİF. Gelen e-faturayı gidere çeviren Muhasebe de seçebilsin diye
    /// OperationsWrite VEYA FinanceWrite (#300; tanım ekranı <c>/gider-turleri</c> OperationsWrite kalır, yazma
    /// değişmedi). Kiracı geneli ana veri; şube alanı yok. Dönen: kimlik + ad + kod.
    /// </summary>
    public async Task<IReadOnlyList<SecimOgesi>> ExpenseCategoryAsync(string? q, int? limit, CancellationToken ct = default)
    {
        FinanceIncludedGate();
        return Filter(await expenseCategories.ListActiveAsync(ct), q, limit, x => x.Ad, x => x.Kod)
            .Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();
    }

    /// <summary>
    /// Satılabilir araç (araç satış formu, #300): <see cref="VehicleStatus.Satildi"/> OLMAYAN araçlar — Blazor satış
    /// formuyla aynı küme (pasif araç satılabilir; satış iptalinde araç durumu geri döner, tek kaynak durum alanıdır).
    /// Şube kapsamı <see cref="VehicleService.ListAsync"/>'te (şubeli kullanıcı yalnız kendi şubesinin aracını görür).
    /// Satış FinanceWrite ister; seçim de aynı izinle açılır.
    /// </summary>
    public async Task<IReadOnlyList<AracSecimOgesi>> SellableVehicleAsync(string? q, int? limit, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceWrite);
        var list = (await vehicles.ListAsync(ct)).Where(v => v.Durum != VehicleStatus.Satildi);
        return Filter(list, q, limit, v => VehicleLabel(v.Plaka, v.Marka, v.Tip), v => v.Plaka, v => v.Grup)
            .Select(v => new AracSecimOgesi(v.Id, VehicleLabel(v.Plaka, v.Marka, v.Tip), v.Plaka, v.Grup, v.Durum.ToString()))
            .ToList();
    }

    /// <summary>Araç grubu (F5 müsaitlik/rezervasyon grup seçimi).</summary>
    public async Task<IReadOnlyList<SecimOgesi>> VehicleGroupAsync(string? q, int? limit, CancellationToken ct = default)
    {
        Gate();
        return Filter(await vehicleGroups.ListActiveAsync(ct), q, limit, g => g.Ad, g => g.Kod)
            .Select(g => new SecimOgesi(g.Id, g.Ad, g.Kod)).ToList();
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary><see cref="MasterDefinitionService{T}"/> tanımları için ortak eşleme (Kod + Ad).</summary>
    private static IReadOnlyList<SecimOgesi> Definition<T>(IEnumerable<T> list, string? q, int? limit) where T : IMasterDefinition
        => Filter(list, q, limit, x => x.Ad, x => x.Kod).Select(x => new SecimOgesi(x.Id, x.Ad, x.Kod)).ToList();

    private static string VehicleLabel(string plate, string? brand, string? tip)
    {
        var extra = $"{brand} {tip}".Trim();
        return extra.Length == 0 ? plate : $"{plate} — {extra}";
    }

    private static string StaffLabel(string name, string soyad) => $"{name} {soyad}".Trim();

    /// <summary>
    /// Bellek-içi süzme (önbellekli master listeleri): Türkçe katlamalı "içerir"; etiketi terimle
    /// BAŞLAYANLAR önce, sonra katlanmış etiket (ordinal — OS yerelinden bağımsız); en çok <see cref="Limit"/> kadar.
    /// </summary>
    private static List<T> Filter<T>(IEnumerable<T> source, string? q, int? limit, Func<T, string> label,
        params Func<T, string?>[] extraFields)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length > MaxQuery) term = term[..MaxQuery];
        term = TurkishText.Normalize(term);

        var candidates = source.Select(x => (Oge: x, Etiket: label(x), Katli: TurkishText.Normalize(label(x))));
        if (term.Length > 0)
            candidates = candidates.Where(a => a.Katli.Contains(term, StringComparison.Ordinal)
                                        || extraFields.Any(f => f(a.Oge) is { } v && TurkishText.Normalize(v).Contains(term, StringComparison.Ordinal)));
        return candidates
            .OrderBy(a => term.Length > 0 && a.Katli.StartsWith(term, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(a => a.Katli, StringComparer.Ordinal)
            .ThenBy(a => a.Etiket, StringComparer.Ordinal)
            .Take(Limit(limit))
            .Select(a => a.Oge)
            .ToList();
    }
}
