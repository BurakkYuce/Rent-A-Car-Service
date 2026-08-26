using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Belge numarası üretimi — sayaç tahsisi + biçimlendirme tek yerde.
///
/// <para><b>Çağıran AKTİF bir transaction içinde olmalı</b> (<see cref="SequenceAllocator"/> ile aynı
/// sözleşme): rollback numarayı geri alır, böylece boşluk oluşmaz. <c>SequenceAllocator</c> ve
/// <c>TenantSequences</c> şeması DEĞİŞMEDİ — günlük sıfırlama anahtara gömülü tarihle sağlanır.</para>
/// </summary>
public static class BelgeNoUretici
{
    /// <summary>
    /// Genel desen (<c>{yyyy}{dd}{MM}{TT}{sss}</c>). Fatura için <see cref="FaturaAsync"/> kullanın.
    /// </summary>
    /// <param name="simdi">
    /// İş anı. Job yollarında job'un kendi "now"ı geçilmeli — aksi halde belge günü ile job günü
    /// ayrışabilir.
    /// </param>
    public static async Task<string> UretAsync(
        AppDbContext db, Guid tenantId, BelgeNoTuru tur, CancellationToken ct, DateTimeOffset? simdi = null)
    {
        // KRİTİK: gün TEK KEZ hesaplanır ve hem sayaç anahtarını hem numara metnini besler.
        // İki kez hesaplanırsa gece yarısına denk gelen bir çağrıda anahtar bir güne, metin başka
        // güne düşer; ertesi gün aynı numara İKİNCİ kez üretilir ve unique index ihlali (ya da daha
        // kötüsü: iki farklı belgede aynı numara) doğar.
        var gun = TenantGun.Gun(simdi ?? DateTimeOffset.UtcNow);
        var n = await SequenceAllocator.NextAsync(db, tenantId, BelgeNo.SayacAnahtari(tur, gun), ct);
        return BelgeNo.Bicimle(tur, gun, n);
    }

    /// <summary>
    /// Fatura numarası — GİB formatı, sayaç SERİ+YIL başına (sıra her yıl 1'den başlar).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Seri kodu yapılandırılmamışsa GÜRÜLTÜLÜ reddeder. Sessiz bir varsayılan ("RNT" gibi) uydurmak,
    /// kalıcı ve değiştirilemez bir mali kayda YANLIŞ seri yazardı — dürüst stub kuralının aynı sınıfı.
    /// </exception>
    public static async Task<string> FaturaAsync(
        AppDbContext db, Guid tenantId, string? seriKodu, CancellationToken ct, DateTimeOffset? simdi = null)
    {
        if (!BelgeNo.SeriGecerliMi(seriKodu))
            throw new InvalidOperationException(
                "Fatura seri kodu tanımlı değil ya da geçersiz. Ayarlar ekranından tam 3 karakterlik " +
                "(A-Z veya 0-9) bir seri kodu girin — e-Fatura fatura numarası bu kodu zorunlu kılar.");

        var yil = TenantGun.Gun(simdi ?? DateTimeOffset.UtcNow).Year;
        var n = await SequenceAllocator.NextAsync(db, tenantId, BelgeNo.FaturaSayacAnahtari(seriKodu!, yil), ct);
        return BelgeNo.FaturaBicimle(seriKodu!, yil, n);
    }
}
