using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F1.4 — "sessiz idempotent başarı" veren yollar için İÇERİK karşılaştırması. Aynı işlem anahtarıyla
/// (SourceId) yazılmış mevcut defter kümesi, gelen kümeyle BİREBİR aynı mı?
///
/// <para><b>Neden:</b> anahtar tek başına "aynı işlem" kanıtı değildir. Aynı anahtar başka cari/tutarla
/// gelirse (istemci anahtarı yenilemedi, iki sekme farklı veriyle, bozuk istemci) sessiz başarı ikinci
/// isteğin parasını YAZMADAN "tamam" derdi. Karşılaştırma yalnız paranın hedefi ve tutarıdır:
/// hesap türü, hesap referansı (cari/kasa/banka), yön, tutar, döviz, kur. Tarih ve açıklama DAHİL
/// DEĞİL — yeniden gönderimde "şimdi" farklıdır ama işlem aynıdır.</para>
///
/// <para>Tutar/kur, DB kolon hassasiyetine (numeric(19,4) / numeric(19,6), PG yuvarlaması: yarım
/// sıfırdan uzağa) yuvarlanarak karşılaştırılır — yoksa 100,12345 gibi bir girişin BİREBİR tekrarı
/// saklanan 100,1235 ile eşleşmez ve meşru yeniden deneme 409 alırdı.</para>
/// </summary>
internal static class LedgerSet
{
    /// <summary>İki küme hedef + tutar bakımından birebir aynı mı (sıra önemsiz, çoklu küme).</summary>
    public static bool Same(IEnumerable<AccountLedgerEntry> existing, IEnumerable<AccountLedgerEntry> incoming)
    {
        var a = existing.Select(Signature).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var b = incoming.Select(Signature).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    /// <summary>Bu kiracıda (RLS + sorgu filtresi) verilen kaynağa ait defter satırları.</summary>
    public static Task<List<AccountLedgerEntry>> ReadAsync(
        AppDbContext db, IReadOnlyCollection<string> sourceTypes, IReadOnlyCollection<Guid> sourceIds, CancellationToken ct)
        => db.AccountLedgerEntries.AsNoTracking()
            .Where(e => sourceTypes.Contains(e.SourceType) && sourceIds.Contains(e.SourceId))
            .ToListAsync(ct);

    private static string Signature(AccountLedgerEntry e) => string.Create(CultureInfo.InvariantCulture,
        $"{(int)e.AccountType}|{e.AccountRef:D}|{(int)e.Direction}|" +
        $"{decimal.Round(e.Amount.Amount, 4, MidpointRounding.AwayFromZero):0.0000}|" +
        $"{(e.Amount.Currency ?? "").Trim().ToUpperInvariant()}|" +
        $"{decimal.Round(e.Amount.Rate, 6, MidpointRounding.AwayFromZero):0.000000}");
}
