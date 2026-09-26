using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Belge numarası üretimi — sayaç tahsisi + biçimlendirme tek yerde.
///
/// <para><b>Çağıran AKTİF bir transaction içinde olmalı</b> (<see cref="SequenceAllocator"/> ile aynı
/// sözleşme): rollback numarayı geri alır, böylece boşluk oluşmaz. <c>SequenceAllocator</c> ve
/// <c>TenantSequences</c> şeması DEĞİŞMEDİ — günlük sıfırlama anahtara gömülü tarihle sağlanır.</para>
/// </summary>
public static class DocumentNoGenerator
{
    /// <summary>
    /// Genel desen (<c>{yyyy}{dd}{MM}{TT}{sss}</c>). Fatura için <see cref="InvoiceAsync"/> kullanın.
    /// </summary>
    /// <param name="now">
    /// İş anı. Job yollarında job'un kendi "now"ı geçilmeli — aksi halde belge günü ile job günü
    /// ayrışabilir.
    /// </param>
    public static async Task<string> GenerateAsync(
        AppDbContext db, Guid tenantId, DocumentNoType type, CancellationToken ct, DateTimeOffset? now = null)
    {
        // KRİTİK: gün TEK KEZ hesaplanır ve hem sayaç anahtarını hem numara metnini besler.
        // İki kez hesaplanırsa gece yarısına denk gelen bir çağrıda anahtar bir güne, metin başka
        // güne düşer; ertesi gün aynı numara İKİNCİ kez üretilir ve unique index ihlali (ya da daha
        // kötüsü: iki farklı belgede aynı numara) doğar.
        var day = TenantDay.Day(now ?? DateTimeOffset.UtcNow);
        var n = await SequenceAllocator.NextAsync(db, tenantId, DocumentNo.CounterKey(type, day), ct);
        return DocumentNo.Format(type, day, n);
    }

    /// <summary>
    /// Fatura numarası — GİB formatı, sayaç SERİ+YIL başına (sıra her yıl 1'den başlar).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Seri kodu yapılandırılmamışsa GÜRÜLTÜLÜ reddeder. Sessiz bir varsayılan ("RNT" gibi) uydurmak,
    /// kalıcı ve değiştirilemez bir mali kayda YANLIŞ seri yazardı — dürüst stub kuralının aynı sınıfı.
    /// </exception>
    public static async Task<string> InvoiceAsync(
        AppDbContext db, Guid tenantId, CancellationToken ct, DateTimeOffset? now = null)
    {
        // Seri kodu AYNI transaction'dan okunur: ayrı bir servis/scope açmak (a) tenant bağlamını
        // yeniden kurmayı gerektirir, (b) TenantSettingsService ManageUsers ister ve fatura kesen
        // kullanıcı çoğu zaman Muhasebe'dir (BildirimKanaliService'te öğrenilen ders).
        var setting = await db.TenantSettings.AsNoTracking()
            .Select(x => x.FaturaSeriKodu).FirstOrDefaultAsync(ct);

        // Ayarlanmamışsa varsayılan seri. Ayar satırı tenant'ta LAZY oluşuyor (ilk kaydetmede),
        // bu yüzden "ayar yok" normal bir durumdur ve fatura kesmeyi engellememelidir.
        // Ayar DOLU ama biçimsizse sessizce varsayılana kaçılmaz — kullanıcı bilerek bir şey
        // yazmış, yanlış seriyle fatura kesmektense gürültülü reddedilir.
        var seriesCode = string.IsNullOrWhiteSpace(setting) ? DocumentNo.DefaultSeries : setting;
        if (!DocumentNo.IsSeriesValid(seriesCode))
            throw new InvalidOperationException(
                $"Fatura seri kodu geçersiz: '{setting}'. Tam 3 karakter olmalı ve yalnız büyük harf " +
                "(A-Z) veya rakam içermelidir (Türkçe karakter kabul edilmez). Ayarlar ekranından düzeltin.");

        var year = TenantDay.Day(now ?? DateTimeOffset.UtcNow).Year;
        var n = await SequenceAllocator.NextAsync(db, tenantId, DocumentNo.InvoiceCounterKey(seriesCode!, year), ct);
        return DocumentNo.FormatInvoice(seriesCode!, year, n);
    }
}
