using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>FAZ-56 — düzeltmenin yönü.</summary>
public enum BalanceAdjustmentDirection
{
    /// <summary>Cariyi ALACAKLANDIR — borcu AZALIR (iyi niyet indirimi, yuvarlama farkı).</summary>
    Alacaklandir = 0,
    /// <summary>Cariyi BORÇLANDIR — borcu ARTAR (eksik faturalanan bedel, düzeltme).</summary>
    Borclandir = 1
}

/// <summary>FAZ-56 — bakiye düzeltme girişi.</summary>
public sealed class BakiyeDuzeltmeInput
{
    public Guid CariId { get; set; }
    public decimal Tutar { get; set; }
    public BalanceAdjustmentDirection Yon { get; set; }
    public string Doviz { get; set; } = "TRY";
    /// <summary>Boş → otomatik çözüm (TRY=1; döviz KurService — 1.1 sözleşmesi).</summary>
    public decimal? Kur { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    /// <summary>Vade (BİLGİ; tahsilat akışına bağlı değil).</summary>
    public DateTimeOffset? Vade { get; set; }
    public string? MakbuzNo { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>Çift-submit koruması — form her render'da yeni GUID basar.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>
/// FAZ-56 — <b>manuel bakiye düzeltmesi</b> (canlı <c>bakiye_islem.aspx</c>): Kasa/Banka'ya
/// DOKUNMADAN tek bir carinin bakiyesini ayarlar.
///
/// <para><b>Karşı hesap kararı (KARARLAR.md FAZ-56):</b> dengeli çiftin diğer bacağı
/// <see cref="LedgerAccountType.MuhasebeDuzeltmesi"/>'dir — özkaynak benzeri AYRI bir tür.
/// <c>Gelir</c>/<c>Gider</c>'e yazmak daha kolay olurdu ama Araç Karnesi / Kârlılık / Filo Analiz
/// raporlarını şişirirdi; CLAUDE.md §6'daki atıf düzeltmesi tam bu sınıf bir hatayı zaten bir kez
/// temizledi. Raporlar P&amp;L'i yalnız Gelir/Gider türünden topladığı için bu tür oralara YAPISAL
/// olarak giremez (kırılgan regresyon testiyle kilitli).</para>
///
/// <para>Yönler:
/// <list type="bullet">
/// <item>Alacaklandır → Borç MuhasebeDuzeltmesi / <b>Alacak Cari</b> (bakiye DÜŞER)</item>
/// <item>Borçlandır  → <b>Borç Cari</b> / Alacak MuhasebeDuzeltmesi (bakiye ARTAR)</item>
/// </list></para>
///
/// <para><b>Düzeltme = ters kayıt.</b> Defter değişmezdir; yanlış bir düzeltme silinmez, ters
/// yönde ikinci bir düzeltmeyle kapatılır.</para>
/// </summary>
public sealed class BalanceAdjustmentService(
    ILedgerPoster ledger, ICustomerRepository customers, ICurrentUser currentUser,
    IPeriodLockGuard periodLock, RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver)
{
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICustomerRepository _customers = customers;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;

    /// <summary>Defterdeki kaynak türü — raporlar ve idempotency indeksi bunu kullanır.</summary>
    public const string Source = "BakiyeDuzeltme";

    /// <summary>
    /// Dengeli düzeltme çiftini postlar. <paramref name="input"/>.IslemAnahtari verilirse aynı
    /// anahtarla ikinci gönderim kısmi unique index'e çarpar ve sessizce yutulur.
    /// </summary>
    public async Task<Guid> AdjustAsync(BakiyeDuzeltmeInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (input.Tutar <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");
        // Para-yolu simetrisi: gelecek tarihli düzeltme hiçbir dönemde mutabık olmaz.
        DatePolicy.MoneyDate(input.Tarih, "Bakiye düzeltme");
        // Vade GELECEKTE olabilir (bilgi alanı) — para tarihi kısıtı ona uygulanmaz.

        if (await _customers.FindAsync(input.CariId, ct) is null)
            throw new ValidationException("Cari bulunamadı.");

        var date = input.Tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date, ct);   // kapalı döneme düzeltme YOK

        // Kur çözümü 1.1 sözleşmesi: açık kur aynen; boş → TRY=1 / döviz KurService (yoksa net red).
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(input.Doviz, input.Kur, input.Tarih, ct);
        var money = new Money(input.Tutar, Kur.ExchangeRateService.NormalizeCodeStrict(input.Doviz), resolvedRate);

        var sourceId = input.IslemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = string.IsNullOrWhiteSpace(input.Aciklama)
            ? (input.Yon == BalanceAdjustmentDirection.Alacaklandir ? "Bakiye düzeltme (alacaklandırma)" : "Bakiye düzeltme (borçlandırma)")
            : input.Aciklama.Trim();
        if (!string.IsNullOrWhiteSpace(input.MakbuzNo)) desc = $"{desc} [{input.MakbuzNo.Trim()}]";

        // Cari bacağının YÖNÜ bakiyeyi belirler; karşı bacak daima onun tersi → küme DENGELİ.
        var accountDirection = input.Yon == BalanceAdjustmentDirection.Alacaklandir
            ? LedgerDirection.Credit    // bakiye düşer
            : LedgerDirection.Debit;    // bakiye artar
        var counterDirection = accountDirection == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit;

        await _ledger.PostAsync(
        [
            new AccountLedgerEntry
            {
                EntryDateUtc = date, AccountType = LedgerAccountType.Cari, AccountRef = input.CariId,
                Direction = accountDirection, Amount = money, SourceType = Source, SourceId = sourceId, Description = desc
            },
            new AccountLedgerEntry
            {
                // Karşı bacak: AccountRef YOK — düzeltme bir varlığa/araca atfedilmez; atıf-hassas
                // raporlara (Karne/Filo Analiz) yanlış bir boyut sızdırmaz.
                EntryDateUtc = date, AccountType = LedgerAccountType.MuhasebeDuzeltmesi, AccountRef = null,
                Direction = counterDirection, Amount = money, SourceType = Source, SourceId = sourceId, Description = desc
            }
        ], ct);
        return sourceId;
    }
}
