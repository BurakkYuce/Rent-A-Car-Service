using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.DisHizmetler;

public interface IDisHizmetRepository
{
    Task<IReadOnlyList<DisHizmetAlimi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default);
    Task<DisHizmetAlimi?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Kayıt + DENGELİ defter kümesi TEK transaction (No tahsisi DH- gapless; IslemAnahtari
    /// kısmi-unique çift-submit çiti — çakışmada ValidationException).</summary>
    Task PostAsync(DisHizmetAlimi kayit, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>İptal: Durum=Iptal + verilen TERS defter kümesi AYNI transaction (silme/update yok).
    /// Zaten iptalse ValidationException (idempotent red).</summary>
    Task IptalAsync(Guid id, IReadOnlyList<AccountLedgerEntry> tersEntries, CancellationToken ct = default);
}

/// <summary>Dış hizmet alımı giriş modeli.</summary>
public sealed class DisHizmetInput
{
    public Guid RentalId { get; set; }
    public Guid FaturaKesilecekCariId { get; set; }
    public Guid? BakiyeliCariId { get; set; }
    public string AlinanHizmet { get; set; } = string.Empty;
    public string? HizmetAlinanFirma { get; set; }
    public decimal HizmetBedeli { get; set; }
    public decimal TedarikciKomisyonOran { get; set; }
    public decimal? BayiKomisyonOran { get; set; }
    public decimal? VerilecekKomisyonTutar { get; set; }
    public string? KomisyonFaturaNo { get; set; }
    public string? BayiFaturaNo { get; set; }
    public bool KdvMuaf { get; set; }
    public string? IndirimTuru { get; set; }
    public string? Doviz { get; set; }
    public decimal? Kur { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? Aciklama { get; set; }
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>
/// B2B dış hizmet alımı iş mantığı (FAZ 4.3). FinanceWrite + dönem kilidi + KurCozucu (1.1 —
/// sessiz kur=1 yok). Defter (SATIR-BAZLI yuvarlama — K2 dersi):
///   Borç Gider(AccountRef = KİRA ARACI, base = bedel×kur) / Alacak Cari(tedarikçi)
///   Borç Cari(tedarikçi) / Alacak Gelir (komisyon = round(bedel × oran/100, 2))
/// Komisyon 0 ise ikinci çift yazılmaz. Karne/Karlilik: gider AccountRef'ten, gelir SourceType
/// "DisHizmet" → RentalId → araç atfı. İptal ters kayıtla (net sıfır).
/// </summary>
public sealed class DisHizmetService(
    IDisHizmetRepository repository,
    Bookings.IBookingRepository bookings,
    Customers.ICustomerRepository cariler,
    ICurrentUser currentUser,
    IPeriodLockGuard periodLock,
    Kur.KurCozucu kurCozucu)
{
    public Task<IReadOnlyList<DisHizmetAlimi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
        => repository.ListForRentalAsync(rentalId, ct);

    public async Task<Guid> CreateAsync(DisHizmetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceWrite);
        if (string.IsNullOrWhiteSpace(input.AlinanHizmet))
            throw new ValidationException("Alınan hizmet zorunludur.");
        if (input.HizmetBedeli <= 0m)
            throw new ValidationException("Hizmet bedeli pozitif olmalıdır.");
        if (input.TedarikciKomisyonOran is < 0m or > 100m)
            throw new ValidationException("Tedarikçi komisyon oranı 0 ile 100 arasında olmalıdır (%).");
        if (input.BayiKomisyonOran is < 0m or > 100m)
            throw new ValidationException("Bayi komisyon oranı 0 ile 100 arasında olmalıdır (%).");
        if (input.VerilecekKomisyonTutar is < 0m)
            throw new ValidationException("Verilecek komisyon tutarı negatif olamaz.");
        TarihPolitikasi.ParaTarihi(input.Tarih, "İşlem");

        var rental = await bookings.FindRentalAsync(input.RentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        Authorization.BranchScope.RequireInScope(currentUser, rental.CikisSubeId, rental.CikisOfisi);
        if (rental.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kiraya dış hizmet kaydı girilemez.");
        if (await cariler.FindAsync(input.FaturaKesilecekCariId, ct) is null)
            throw new ValidationException("Tedarikçi cari bulunamadı.");

        var tarih = input.Tarih ?? DateTimeOffset.UtcNow;
        await periodLock.EnsureOpenAsync(tarih, ct);
        // 1.1 kur otomatiği: açık kur aynen; boş → TRY=1 / döviz çözümü; çözülemezse temiz red.
        var kur = await kurCozucu.CozAsync(input.Doviz, input.Kur, tarih, ct);
        var doviz = Kur.KurService.NormalizeKodStrict(input.Doviz);

        var kayit = new DisHizmetAlimi
        {
            RentalId = rental.Id,
            FaturaKesilecekCariId = input.FaturaKesilecekCariId,
            BakiyeliCariId = input.BakiyeliCariId,
            AlinanHizmet = input.AlinanHizmet.Trim(),
            HizmetAlinanFirma = TrimOrNull(input.HizmetAlinanFirma),
            HizmetBedeli = input.HizmetBedeli,
            TedarikciKomisyonOran = input.TedarikciKomisyonOran,
            BayiKomisyonOran = input.BayiKomisyonOran,
            VerilecekKomisyonTutar = input.VerilecekKomisyonTutar,
            KomisyonFaturaNo = TrimOrNull(input.KomisyonFaturaNo),
            BayiFaturaNo = TrimOrNull(input.BayiFaturaNo),
            KdvMuaf = input.KdvMuaf,
            IndirimTuru = TrimOrNull(input.IndirimTuru),
            Currency = doviz,
            Kur = kur,
            Tarih = tarih,
            Aciklama = TrimOrNull(input.Aciklama),
            IslemAnahtari = input.IslemAnahtari is { } k && k != Guid.Empty ? k : null
        };
        await repository.PostAsync(kayit, Entries(kayit, rental.VehicleId, flip: false), ct);
        return kayit.Id;
    }

    /// <summary>İptal — ters kayıt (net sıfır); kayıt Durum=Iptal (silme yok).</summary>
    public async Task IptalEtAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceReverse); // inceltme: ters kayıt yazar
        var kayit = await repository.FindAsync(id, ct)
            ?? throw new ValidationException("Dış hizmet kaydı bulunamadı.");
        // F4.4a adversarial L5: kapsam, durumdan ÖNCE — başka şubenin kaydının iptal edilmiş olduğu
        // "zaten iptal" mesajıyla sızmasın; kapsam dışı her kayıt aynı 403'ü alır.
        var rental = await bookings.FindRentalAsync(kayit.RentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        Authorization.BranchScope.RequireInScope(currentUser, rental.CikisSubeId, rental.CikisOfisi);
        if (kayit.Durum == DisHizmetDurum.Iptal)
            throw new ValidationException("Kayıt zaten iptal edilmiş.");
        await periodLock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // ters kayıt bugüne yazılır
        await repository.IptalAsync(id, Entries(kayit, rental.VehicleId, flip: true, tarih: DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Komisyon = round(bedel × oran / 100, 2) — SATIR-BAZLI yuvarlama (K2).</summary>
    public static decimal Komisyon(decimal bedel, decimal oran)
        => Math.Round(bedel * oran / 100m, 2, MidpointRounding.AwayFromZero);

    private static List<AccountLedgerEntry> Entries(DisHizmetAlimi k, Guid vehicleId, bool flip, DateTimeOffset? tarih = null)
    {
        var t = tarih ?? k.Tarih;
        // Ters kayıtta SourceType KORUNUR ("DisHizmet") — karne/Karlilik atfı SourceId'den kayda gider;
        // yön çevrimi signed toplamda netler ("TersKayit" tipi atfı koparıp iptali raporda bırakıyordu).
        AccountLedgerEntry E(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = t, AccountType = type, AccountRef = reff,
            Direction = flip ? (dir == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit) : dir,
            Amount = new Money(amount, k.Currency, k.Kur),
            SourceType = "DisHizmet", SourceId = k.Id,
            Description = $"Dış hizmet {k.No} — {k.AlinanHizmet}" + (flip ? " (iptal — ters kayıt)" : "")
        };

        var entries = new List<AccountLedgerEntry>
        {
            // Hizmet bedeli: bize maliyet (gider ARAÇTA — karne/Karlilik) / tedarikçiye borçlanırız.
            E(LedgerAccountType.Gider, vehicleId, LedgerDirection.Debit, k.HizmetBedeli),
            E(LedgerAccountType.Cari, k.FaturaKesilecekCariId, LedgerDirection.Credit, k.HizmetBedeli)
        };
        var komisyon = Komisyon(k.HizmetBedeli, k.TedarikciKomisyonOran);
        if (komisyon > 0m)
        {
            // Komisyon geliri: tedarikçiden alacaklanırız / gelir (karne atfı SourceType "DisHizmet").
            entries.Add(E(LedgerAccountType.Cari, k.FaturaKesilecekCariId, LedgerDirection.Debit, komisyon));
            entries.Add(E(LedgerAccountType.Gelir, null, LedgerDirection.Credit, komisyon));
        }
        return entries;
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
