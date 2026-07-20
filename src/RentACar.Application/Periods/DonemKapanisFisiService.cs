using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Periods;

/// <summary>Kapanış önizlemesi: kapanış tarihine kadarki net gelir/gider ve dönem sonucu (fiş atmaz).</summary>
public sealed record DonemKapanisOnizleme(decimal Gelir, decimal Gider, decimal DonemSonucu);

/// <summary>
/// Dönem-sonu kapanış (close-lite, PR-A) uygulama servisi: yetki + ön-doğrulama yapar, atomik fiş+kilit işini
/// <see cref="IDonemKapanisRepository"/>'ye devreder. Kapanış Gelir/Gider bakiyelerini sıfırlayıp net dönem
/// sonucunu <see cref="LedgerAccountType.DonemSonucu"/> (özkaynak benzeri) hesabına taşır; YALNIZ P&amp;L
/// kapatılır (KDV/Kasa/Banka/Cari bilanço hesabı dokunulmaz). Fiş <c>SourceType='DonemKapanis'</c> → P&amp;L
/// raporlarından (GelirGider + Karlılık) HARİÇ tutulur (iç virman).
///
/// ÇOK-DÖNEM / İDEMPOTENCY / EŞZAMANLILIK repo'da tenant-başına advisory-lock ile serileştirilmiş şekilde
/// çözülür (kapanış her seferinde GÜNCEL bakiyeyi sıfırlar → delta yakalar, çift saymaz; yeniden-kapatma taze
/// SourceId ile meşru delta'yı yakalar). Değişmez defter: düzeltme = ileri tarihe kapat / ters kayıt.
/// </summary>
public sealed class DonemKapanisFisiService(
    IReportRepository reports, IDonemKapanisRepository repo, DonemKilidiService donem,
    ICurrentUser currentUser, ScreenPermissionService screens)
{
    public const string SourceTypeAdi = "DonemKapanis";

    /// <summary>Kapanış önizlemesi (fiş atmaz): kapanış tarihine kadarki GÜNCEL Gelir/Gider bakiyesi (kapanışın
    /// gerçekten sıfırlayacağı tutar) ve net dönem sonucu.</summary>
    public async Task<DonemKapanisOnizleme> OnizleAsync(DateTimeOffset kapanisTarihi, CancellationToken ct = default)
    {
        var (gelirSigned, giderSigned) = await BakiyeOkuAsync(KapanisAni(kapanisTarihi), ct);
        // P&L: gelir = −Σ SignedBase(Gelir) (gelir Alacak → negatif signed); gider = +Σ SignedBase(Gider).
        var gelir = -gelirSigned;
        var gider = giderSigned;
        return new DonemKapanisOnizleme(gelir, gider, gelir - gider);
    }

    /// <summary>Dönemi kapat: yetki + "zaten kapalı" ön-kontrolü → atomik fiş+kilit (repo).</summary>
    public async Task KapatAsync(DateTimeOffset kapanisTarihi, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceWrite);
        await screens.EnsureScreenAccessAsync("donem-kapanis", Permission.FinanceWrite, ct);

        // Zaten kapalı mı? (aynı/önceki tarih kilitliyse yeniden kapatma — UX; asıl güvence repo'daki serialization.)
        var mevcut = await donem.GetClosingDateAsync(ct);
        if (mevcut is { } m && kapanisTarihi.Date <= m.Date)
            throw new ValidationException($"Dönem zaten {m:yyyy-MM-dd} tarihine kapalı. Yeniden kapatmak için önce kilidi kaldırın.");

        await repo.KapatAsync(kapanisTarihi, ct);
    }

    /// <summary>Kapanış anına (dahil) kadarki Gelir/Gider GÜNCEL SignedBase bakiyeleri (önceki kapanışlar DAHİL).</summary>
    private async Task<(decimal gelirSigned, decimal giderSigned)> BakiyeOkuAsync(DateTimeOffset kapanisAni, CancellationToken ct)
    {
        var rows = await reports.GetLedgerRowsAsync(
            [LedgerAccountType.Gelir, LedgerAccountType.Gider], null, kapanisAni, ct);
        decimal Signed(LedgerAccountType t) => rows.Where(r => r.AccountType == t)
            .Sum(r => r.Direction == LedgerDirection.Debit ? r.Base : -r.Base);
        return (Signed(LedgerAccountType.Gelir), Signed(LedgerAccountType.Gider));
    }

    /// <summary>Kapanış tarihinin UTC gün SONU (23:59:59.9999999) — repo ile AYNI (o günün tüm kayıtları dahil).</summary>
    private static DateTimeOffset KapanisAni(DateTimeOffset kapanisTarihi)
        => new DateTimeOffset(kapanisTarihi.UtcDateTime.Date, TimeSpan.Zero).AddDays(1).AddTicks(-1);
}
