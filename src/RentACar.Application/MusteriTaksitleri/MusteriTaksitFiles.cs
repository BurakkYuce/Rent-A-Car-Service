using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.MusteriTaksitleri;

/// <summary>Müşteri taksiti kalıcılığı (FAZ-66).</summary>
public interface ICustomerInstallmentRepository
{
    Task<IReadOnlyList<MusteriTaksit>> SearchAsync(MusteriTaksitFilter filter, CancellationToken ct = default);
    Task<MusteriTaksit?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(MusteriTaksit row, CancellationToken ct = default);
    /// <summary>Plan üretimi: N taksit TEK transaction'da (yarım plan kalmasın).</summary>
    Task CreateManyAsync(IReadOnlyList<MusteriTaksit> rows, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<MusteriTaksit> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Bir cari+araç için mevcut en yüksek sıra (plan üretiminde devam etmek için).</summary>
    Task<int> LastOrderAsync(Guid customerId, Guid? vehicleId, CancellationToken ct = default);

    /// <summary>F6.1b — satır kilidi + (doluysa) xmin sürüm karşılaştırması altında güncelleme.</summary>
    Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<MusteriTaksit> apply, CancellationToken ct = default);

    /// <summary>F6.1b — satır sürümü (xmin); yoksa null.</summary>
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Taksit listesi filtresi. Boş alan = kısıt yok.</summary>
public sealed class MusteriTaksitFilter
{
    public Guid? CariId { get; set; }
    public Guid? VehicleId { get; set; }
    public InstallmentStatus? Durum { get; set; }
    public DateTimeOffset? VadeMin { get; set; }
    public DateTimeOffset? VadeMax { get; set; }
    /// <summary>true → yalnız GECİKENLER (vadesi geçmiş + ödenmemiş). Türetilmiş, bellekte süzülür.</summary>
    public bool? SadeceGecikmis { get; set; }
}

/// <summary>Tek taksit giriş modeli.</summary>
public sealed class MusteriTaksitInput
{
    public Guid CariId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? VehicleSaleId { get; set; }
    public DateTimeOffset? Vade { get; set; }
    public decimal TaksitTutari { get; set; }
    public string? Currency { get; set; }
    public decimal? Kur { get; set; }
    public InstallmentStatus Durum { get; set; } = InstallmentStatus.Bekliyor;
    public DateTimeOffset? OdemeTarihi { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>F6.1b — <c>/api/ui</c> çift gönderim anahtarı; doluysa taksidin Id'si olur (ikinci oluşturma → 409).
    /// Güncellemede yok sayılır.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>Plan üretimi girdisi: N eşit taksit, aylık vade.</summary>
public sealed class TaksitPlanInput
{
    public Guid CariId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? VehicleSaleId { get; set; }
    /// <summary>Toplam tutar — taksitlere BÖLÜNÜR (kalan-yöntemi, bkz. servis).</summary>
    public decimal ToplamTutar { get; set; }
    public int TaksitSayisi { get; set; }
    public DateTimeOffset? IlkVade { get; set; }
    public string? Currency { get; set; }
    public decimal? Kur { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>F6.1b — <c>/api/ui</c> çift gönderim anahtarı. Doluysa planın taksit Id'leri ondan TÜRETİLİR
    /// (<see cref="CustomerInstallmentService.PlanLineId"/>): aynı anahtarla ikinci plan PK'ye çarpar → 409, yarım plan olmaz.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>Cari bazlı özet (liste başlığı).</summary>
public sealed record TaksitOzet(int Adet, int OdenenAdet, int GecikenAdet,
    decimal ToplamBaz, decimal OdenenBaz, decimal KalanBaz);

/// <summary>
/// Müşteri taksit takibi (FAZ-66).
///
/// <para><b>DEFTERE YAZMAZ.</b> "Ödendi" işareti takip amaçlıdır; cari bakiyeyi Kasa/Banka
/// tahsilat akışı değiştirir. İkisini bağlamak çift-kayıt üretirdi. Yetki yine
/// <see cref="Permission.FinanceWrite"/> — para BİLGİSİ taşıyor ve operasyon rolüne açılmamalı.</para>
/// </summary>
public sealed class CustomerInstallmentService(
    ICustomerInstallmentRepository repository, ICurrentUser currentUser)
{
    private readonly ICustomerInstallmentRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Plan üretiminde üst sınır — 600 taksit (50 yıl) üzeri gerçek bir iş değil.</summary>
    public const int MaxInstallments = 600;

    public async Task<IReadOnlyList<MusteriTaksit>> SearchAsync(
        MusteriTaksitFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.SearchAsync(filter ?? new MusteriTaksitFilter(), ct);
    }

    public async Task<MusteriTaksit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.FindAsync(id, ct);
    }

    /// <summary>Liste özeti — tutarlar BAZ para (Tutar × Kur); karışık dövizde toplanabilsin.</summary>
    public static TaksitOzet Summary(IEnumerable<MusteriTaksit> rows)
    {
        var l = rows.ToList();
        var paid = l.Where(x => x.Durum == InstallmentStatus.Odendi).ToList();
        return new TaksitOzet(
            l.Count, paid.Count, l.Count(x => x.Gecikti),
            l.Sum(x => x.TutarBaz), paid.Sum(x => x.TutarBaz),
            l.Where(x => x.Durum != InstallmentStatus.Odendi).Sum(x => x.TutarBaz));
    }

    public async Task<Guid> CreateAsync(MusteriTaksitInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var row = new MusteriTaksit
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            Sira = await _repository.LastOrderAsync(input.CariId, input.VehicleId, ct) + 1,
        };
        Apply(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, MusteriTaksitInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var copy = new MusteriTaksit();
        Apply(copy, input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.CariId = copy.CariId; r.VehicleId = copy.VehicleId; r.VehicleSaleId = copy.VehicleSaleId;
            r.Vade = copy.Vade; r.TaksitTutari = copy.TaksitTutari;
            r.Currency = copy.Currency; r.Kur = copy.Kur;
            r.Durum = copy.Durum; r.OdemeTarihi = copy.OdemeTarihi; r.Aciklama = copy.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Ödendi/geri-al işareti. Deftere DOKUNMAZ (takip bayrağı).</summary>
    public async Task<bool> MarkPaidAsync(Guid id, bool paid, DateTimeOffset? date = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (paid) DatePolicy.MoneyDate(date, "Taksit ödeme");
        return await _repository.UpdateAsync(id, r =>
        {
            r.Durum = paid ? InstallmentStatus.Odendi : InstallmentStatus.Bekliyor;
            // Geri alındığında ödeme tarihi de TEMİZLENİR; kalsaydı "beklemede ama ödeme tarihi var"
            // gibi kendi içinde çelişen bir satır kalırdı.
            r.OdemeTarihi = paid ? (date ?? DateTimeOffset.UtcNow) : null;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return await _repository.DeleteAsync(id, ct);
    }

    /// <summary>
    /// N eşit aylık taksitlik plan üretir. <b>KALAN-YÖNTEMİ:</b> son taksit = toplam − (n−1)×aylık →
    /// Σ taksit kuruş-birebir toplama eşit (yuvarlama kayması birikmez; AracKredi 1.3 dersi).
    /// </summary>
    public async Task<int> GeneratePlanAsync(TaksitPlanInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Müşteri seçilmeli.");
        if (input.TaksitSayisi is < 1 or > MaxInstallments)
            throw new ValidationException($"Taksit sayısı 1 ile {MaxInstallments} arasında olmalıdır.");
        if (input.ToplamTutar <= 0m) throw new ValidationException("Toplam tutar pozitif olmalıdır.");

        var first = input.IlkVade ?? DateTimeOffset.UtcNow;
        var currency = NormalizeCurrency(input.Currency);
        var exchangeRate = CheckExchangeRate(input.Kur);
        var monthly = decimal.Round(input.ToplamTutar / input.TaksitSayisi, 2, MidpointRounding.AwayFromZero);
        var startSequence = await _repository.LastOrderAsync(input.CariId, input.VehicleId, ct);

        var rows = new List<MusteriTaksit>(input.TaksitSayisi);
        for (var i = 1; i <= input.TaksitSayisi; i++)
        {
            var amount = i == input.TaksitSayisi
                ? decimal.Round(input.ToplamTutar - monthly * (input.TaksitSayisi - 1), 2, MidpointRounding.AwayFromZero)
                : monthly;
            rows.Add(new MusteriTaksit
            {
                Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? PlanLineId(ia, i) : Guid.NewGuid(),
                CariId = input.CariId, VehicleId = input.VehicleId, VehicleSaleId = input.VehicleSaleId,
                Sira = startSequence + i, Vade = first.AddMonths(i - 1), TaksitTutari = amount,
                Currency = currency, Kur = exchangeRate, Aciklama = Trim(input.Aciklama)
            });
        }
        await _repository.CreateManyAsync(rows, ct);
        return rows.Count;
    }

    /// <summary>F6.1b — plan satırı Id'si: 1. satır anahtarın KENDİSİ (mükerrer denetimi onu bulur), sonrakiler
    /// UUIDv5(anahtar, "plan|i"). Saf; aynı anahtar her zaman aynı kimlik kümesini verir.</summary>
    public static Guid PlanLineId(Guid key, int order)
        => order == 1 ? key : OperationKeyDeriver.UuidV5(key, $"plan|{order}");

    /// <summary>F6.1b — kayıt sürümü (xmin).</summary>
    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.VersionAsync(id, ct);
    }

    /// <summary>F6.1b — <see cref="UpdateAsync"/>'in sürümlü, KİLİTLİ karşılığı (/api/ui PUT): bayat sürüm → 409
    /// <c>cakisma</c>; başka oturumun ödeme işaretini ya da tutar değişikliğini sessizce ezmez.</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, MusteriTaksitInput input, string version, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var copy = new MusteriTaksit();
        Apply(copy, input);
        return await _repository.UpdateLockedAsync(id, version, r =>
        {
            r.CariId = copy.CariId; r.VehicleId = copy.VehicleId; r.VehicleSaleId = copy.VehicleSaleId;
            r.Vade = copy.Vade; r.TaksitTutari = copy.TaksitTutari;
            r.Currency = copy.Currency; r.Kur = copy.Kur;
            r.Durum = copy.Durum; r.OdemeTarihi = copy.OdemeTarihi; r.Aciklama = copy.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Zaten ödendi işaretli taksidin ikinci "ödendi" isteği.</summary>
    public const string AlreadyPaidMessage = "Bu taksit zaten ödendi olarak işaretlenmiş (No #{0}, {1} {2}); yeniden işaretlenmedi.";

    /// <summary>
    /// F6.1b — ödendi/geri-al işareti KİLİT ALTINDA (/api/ui). Deftere DOKUNMAZ (takip bayrağı). Blazor yolu ikinci
    /// "ödendi"de ödeme tarihini sessizce yeniden yazıyordu; burada ödenmiş taksidin tekrar işaretlenmesi 409
    /// <c>mukerrer</c> + <c>mevcut</c> (aynı tarih ya da tarihsiz tekrar → <c>ayniIcerik</c>). Beklemedeki taksidi
    /// geri almak yapısal no-op.
    /// </summary>
    /// <param name="lockedGuard">F6.1b adversarial L4: kilit ALTINDA çağrılır, durumdan ÖNCE (ör. uç, kapsamı
    /// denetlediği araç kimliğinin hâlâ aynı olduğunu doğrular — arada başka oturum taksidi başka şubenin aracına
    /// taşıdıysa işlem yapılmaz).</param>
    public async Task<bool> MarkPaidLockedAsync(Guid id, bool paid, DateTimeOffset? date = null,
        CancellationToken ct = default, Action<MusteriTaksit>? lockedGuard = null)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (paid) DatePolicy.MoneyDate(date, "Taksit ödeme");
        return await _repository.UpdateLockedAsync(id, null, r =>
        {
            lockedGuard?.Invoke(r);
            if (paid && r.Durum == InstallmentStatus.Odendi)
            {
                var same = date is null || r.OdemeTarihi is { } ot
                    && ot.UtcTicks / TimeSpan.TicksPerMicrosecond == date.Value.UtcTicks / TimeSpan.TicksPerMicrosecond;
                var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
                throw new DuplicateOperationException(
                    string.Format(tr, AlreadyPaidMessage, r.Sira, r.TaksitTutari.ToString("N2", tr), r.Currency),
                    new MevcutIslem(r.Id, $"#{r.Sira}", r.TaksitTutari, r.Currency, same));
            }
            if (!paid && r.Durum != InstallmentStatus.Odendi) return; // zaten beklemede: no-op
            r.Durum = paid ? InstallmentStatus.Odendi : InstallmentStatus.Bekliyor;
            r.OdemeTarihi = paid ? (date ?? DateTimeOffset.UtcNow) : null;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    private static void Apply(MusteriTaksit row, MusteriTaksitInput n)
    {
        if (n.CariId == Guid.Empty) throw new ValidationException("Müşteri seçilmeli.");
        if (n.TaksitTutari <= 0m) throw new ValidationException("Taksit tutarı pozitif olmalıdır.");
        if (n.Vade is null) throw new ValidationException("Vade zorunludur.");
        if (n.Durum == InstallmentStatus.Odendi) DatePolicy.MoneyDate(n.OdemeTarihi, "Taksit ödeme");

        row.CariId = n.CariId;
        row.VehicleId = n.VehicleId;
        row.VehicleSaleId = n.VehicleSaleId;
        row.Vade = n.Vade.Value;
        row.TaksitTutari = decimal.Round(n.TaksitTutari, 2, MidpointRounding.AwayFromZero);
        row.Currency = NormalizeCurrency(n.Currency);
        row.Kur = CheckExchangeRate(n.Kur);
        row.Durum = n.Durum;
        // Beklemede satırda ödeme tarihi TUTULMAZ (kendi içinde çelişen kayıt olmasın).
        row.OdemeTarihi = n.Durum == InstallmentStatus.Odendi ? (n.OdemeTarihi ?? DateTimeOffset.UtcNow) : null;
        row.Aciklama = Trim(n.Aciklama);
    }

    private static string NormalizeCurrency(string? c)
        => string.IsNullOrWhiteSpace(c) ? "TRY" : c.Trim().ToUpperInvariant();

    /// <summary>Kur POZİTİF olmalı: 0/negatif kur baz tutarı sıfırlar ya da ters çevirir.</summary>
    private static decimal CheckExchangeRate(decimal? exchangeRate)
    {
        var k = exchangeRate ?? 1m;
        if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        return k;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
