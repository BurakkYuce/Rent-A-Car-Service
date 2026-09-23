using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.MusteriTaksitleri;

/// <summary>Müşteri taksiti kalıcılığı (FAZ-66).</summary>
public interface IMusteriTaksitRepository
{
    Task<IReadOnlyList<MusteriTaksit>> SearchAsync(MusteriTaksitFilter filtre, CancellationToken ct = default);
    Task<MusteriTaksit?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(MusteriTaksit row, CancellationToken ct = default);
    /// <summary>Plan üretimi: N taksit TEK transaction'da (yarım plan kalmasın).</summary>
    Task CreateManyAsync(IReadOnlyList<MusteriTaksit> rows, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<MusteriTaksit> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Bir cari+araç için mevcut en yüksek sıra (plan üretiminde devam etmek için).</summary>
    Task<int> SonSiraAsync(Guid cariId, Guid? vehicleId, CancellationToken ct = default);

    /// <summary>F6.1b — satır kilidi + (doluysa) xmin sürüm karşılaştırması altında güncelleme.</summary>
    Task<bool> KilitliGuncelleAsync(Guid id, string? beklenenSurum, Action<MusteriTaksit> apply, CancellationToken ct = default);

    /// <summary>F6.1b — satır sürümü (xmin); yoksa null.</summary>
    Task<string?> SurumAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Taksit listesi filtresi. Boş alan = kısıt yok.</summary>
public sealed class MusteriTaksitFilter
{
    public Guid? CariId { get; set; }
    public Guid? VehicleId { get; set; }
    public TaksitDurum? Durum { get; set; }
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
    public TaksitDurum Durum { get; set; } = TaksitDurum.Bekliyor;
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
    /// (<see cref="MusteriTaksitService.PlanSatirId"/>): aynı anahtarla ikinci plan PK'ye çarpar → 409, yarım plan olmaz.</summary>
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
public sealed class MusteriTaksitService(
    IMusteriTaksitRepository repository, ICurrentUser currentUser)
{
    private readonly IMusteriTaksitRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Plan üretiminde üst sınır — 600 taksit (50 yıl) üzeri gerçek bir iş değil.</summary>
    public const int MaxTaksit = 600;

    public async Task<IReadOnlyList<MusteriTaksit>> SearchAsync(
        MusteriTaksitFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.SearchAsync(filtre ?? new MusteriTaksitFilter(), ct);
    }

    public async Task<MusteriTaksit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.FindAsync(id, ct);
    }

    /// <summary>Liste özeti — tutarlar BAZ para (Tutar × Kur); karışık dövizde toplanabilsin.</summary>
    public static TaksitOzet Ozet(IEnumerable<MusteriTaksit> satirlar)
    {
        var l = satirlar.ToList();
        var odenen = l.Where(x => x.Durum == TaksitDurum.Odendi).ToList();
        return new TaksitOzet(
            l.Count, odenen.Count, l.Count(x => x.Gecikti),
            l.Sum(x => x.TutarBaz), odenen.Sum(x => x.TutarBaz),
            l.Where(x => x.Durum != TaksitDurum.Odendi).Sum(x => x.TutarBaz));
    }

    public async Task<Guid> CreateAsync(MusteriTaksitInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var row = new MusteriTaksit
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            Sira = await _repository.SonSiraAsync(input.CariId, input.VehicleId, ct) + 1,
        };
        Uygula(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, MusteriTaksitInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var kopya = new MusteriTaksit();
        Uygula(kopya, input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.CariId = kopya.CariId; r.VehicleId = kopya.VehicleId; r.VehicleSaleId = kopya.VehicleSaleId;
            r.Vade = kopya.Vade; r.TaksitTutari = kopya.TaksitTutari;
            r.Currency = kopya.Currency; r.Kur = kopya.Kur;
            r.Durum = kopya.Durum; r.OdemeTarihi = kopya.OdemeTarihi; r.Aciklama = kopya.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Ödendi/geri-al işareti. Deftere DOKUNMAZ (takip bayrağı).</summary>
    public async Task<bool> OdemeIsaretleAsync(Guid id, bool odendi, DateTimeOffset? tarih = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (odendi) TarihPolitikasi.ParaTarihi(tarih, "Taksit ödeme");
        return await _repository.UpdateAsync(id, r =>
        {
            r.Durum = odendi ? TaksitDurum.Odendi : TaksitDurum.Bekliyor;
            // Geri alındığında ödeme tarihi de TEMİZLENİR; kalsaydı "beklemede ama ödeme tarihi var"
            // gibi kendi içinde çelişen bir satır kalırdı.
            r.OdemeTarihi = odendi ? (tarih ?? DateTimeOffset.UtcNow) : null;
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
    public async Task<int> PlanUretAsync(TaksitPlanInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Müşteri seçilmeli.");
        if (input.TaksitSayisi is < 1 or > MaxTaksit)
            throw new ValidationException($"Taksit sayısı 1 ile {MaxTaksit} arasında olmalıdır.");
        if (input.ToplamTutar <= 0m) throw new ValidationException("Toplam tutar pozitif olmalıdır.");

        var ilk = input.IlkVade ?? DateTimeOffset.UtcNow;
        var currency = ParaBirimi(input.Currency);
        var kur = KurKontrol(input.Kur);
        var aylik = decimal.Round(input.ToplamTutar / input.TaksitSayisi, 2, MidpointRounding.AwayFromZero);
        var baslangicSira = await _repository.SonSiraAsync(input.CariId, input.VehicleId, ct);

        var rows = new List<MusteriTaksit>(input.TaksitSayisi);
        for (var i = 1; i <= input.TaksitSayisi; i++)
        {
            var tutar = i == input.TaksitSayisi
                ? decimal.Round(input.ToplamTutar - aylik * (input.TaksitSayisi - 1), 2, MidpointRounding.AwayFromZero)
                : aylik;
            rows.Add(new MusteriTaksit
            {
                Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? PlanSatirId(ia, i) : Guid.NewGuid(),
                CariId = input.CariId, VehicleId = input.VehicleId, VehicleSaleId = input.VehicleSaleId,
                Sira = baslangicSira + i, Vade = ilk.AddMonths(i - 1), TaksitTutari = tutar,
                Currency = currency, Kur = kur, Aciklama = Trim(input.Aciklama)
            });
        }
        await _repository.CreateManyAsync(rows, ct);
        return rows.Count;
    }

    /// <summary>F6.1b — plan satırı Id'si: 1. satır anahtarın KENDİSİ (mükerrer denetimi onu bulur), sonrakiler
    /// UUIDv5(anahtar, "plan|i"). Saf; aynı anahtar her zaman aynı kimlik kümesini verir.</summary>
    public static Guid PlanSatirId(Guid anahtar, int sira)
        => sira == 1 ? anahtar : IslemAnahtariTuretici.UuidV5(anahtar, $"plan|{sira}");

    /// <summary>F6.1b — kayıt sürümü (xmin).</summary>
    public async Task<string?> SurumAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.FinanceWrite, Permission.ViewReports);
        return await _repository.SurumAsync(id, ct);
    }

    /// <summary>F6.1b — <see cref="UpdateAsync"/>'in sürümlü, KİLİTLİ karşılığı (/api/ui PUT): bayat sürüm → 409
    /// <c>cakisma</c>; başka oturumun ödeme işaretini ya da tutar değişikliğini sessizce ezmez.</summary>
    public async Task<bool> UpdateSurumluAsync(Guid id, MusteriTaksitInput input, string surum, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var kopya = new MusteriTaksit();
        Uygula(kopya, input);
        return await _repository.KilitliGuncelleAsync(id, surum, r =>
        {
            r.CariId = kopya.CariId; r.VehicleId = kopya.VehicleId; r.VehicleSaleId = kopya.VehicleSaleId;
            r.Vade = kopya.Vade; r.TaksitTutari = kopya.TaksitTutari;
            r.Currency = kopya.Currency; r.Kur = kopya.Kur;
            r.Durum = kopya.Durum; r.OdemeTarihi = kopya.OdemeTarihi; r.Aciklama = kopya.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Zaten ödendi işaretli taksidin ikinci "ödendi" isteği.</summary>
    public const string ZatenOdendiMesaji = "Bu taksit zaten ödendi olarak işaretlenmiş (No #{0}, {1} {2}); yeniden işaretlenmedi.";

    /// <summary>
    /// F6.1b — ödendi/geri-al işareti KİLİT ALTINDA (/api/ui). Deftere DOKUNMAZ (takip bayrağı). Blazor yolu ikinci
    /// "ödendi"de ödeme tarihini sessizce yeniden yazıyordu; burada ödenmiş taksidin tekrar işaretlenmesi 409
    /// <c>mukerrer</c> + <c>mevcut</c> (aynı tarih ya da tarihsiz tekrar → <c>ayniIcerik</c>). Beklemedeki taksidi
    /// geri almak yapısal no-op.
    /// </summary>
    public async Task<bool> OdemeIsaretleKilitliAsync(Guid id, bool odendi, DateTimeOffset? tarih = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (odendi) TarihPolitikasi.ParaTarihi(tarih, "Taksit ödeme");
        return await _repository.KilitliGuncelleAsync(id, null, r =>
        {
            if (odendi && r.Durum == TaksitDurum.Odendi)
            {
                var ayni = tarih is null || r.OdemeTarihi is { } ot
                    && ot.UtcTicks / TimeSpan.TicksPerMicrosecond == tarih.Value.UtcTicks / TimeSpan.TicksPerMicrosecond;
                var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
                throw new MukerrerIslemException(
                    string.Format(tr, ZatenOdendiMesaji, r.Sira, r.TaksitTutari.ToString("N2", tr), r.Currency),
                    new MevcutIslem(r.Id, $"#{r.Sira}", r.TaksitTutari, r.Currency, ayni));
            }
            if (!odendi && r.Durum != TaksitDurum.Odendi) return; // zaten beklemede: no-op
            r.Durum = odendi ? TaksitDurum.Odendi : TaksitDurum.Bekliyor;
            r.OdemeTarihi = odendi ? (tarih ?? DateTimeOffset.UtcNow) : null;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    private static void Uygula(MusteriTaksit row, MusteriTaksitInput n)
    {
        if (n.CariId == Guid.Empty) throw new ValidationException("Müşteri seçilmeli.");
        if (n.TaksitTutari <= 0m) throw new ValidationException("Taksit tutarı pozitif olmalıdır.");
        if (n.Vade is null) throw new ValidationException("Vade zorunludur.");
        if (n.Durum == TaksitDurum.Odendi) TarihPolitikasi.ParaTarihi(n.OdemeTarihi, "Taksit ödeme");

        row.CariId = n.CariId;
        row.VehicleId = n.VehicleId;
        row.VehicleSaleId = n.VehicleSaleId;
        row.Vade = n.Vade.Value;
        row.TaksitTutari = decimal.Round(n.TaksitTutari, 2, MidpointRounding.AwayFromZero);
        row.Currency = ParaBirimi(n.Currency);
        row.Kur = KurKontrol(n.Kur);
        row.Durum = n.Durum;
        // Beklemede satırda ödeme tarihi TUTULMAZ (kendi içinde çelişen kayıt olmasın).
        row.OdemeTarihi = n.Durum == TaksitDurum.Odendi ? (n.OdemeTarihi ?? DateTimeOffset.UtcNow) : null;
        row.Aciklama = Trim(n.Aciklama);
    }

    private static string ParaBirimi(string? c)
        => string.IsNullOrWhiteSpace(c) ? "TRY" : c.Trim().ToUpperInvariant();

    /// <summary>Kur POZİTİF olmalı: 0/negatif kur baz tutarı sıfırlar ya da ters çevirir.</summary>
    private static decimal KurKontrol(decimal? kur)
    {
        var k = kur ?? 1m;
        if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
        return k;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
