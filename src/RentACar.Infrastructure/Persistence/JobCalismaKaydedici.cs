using Microsoft.EntityFrameworkCore;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Arka plan üretici koşularını <c>JobCalismaLoglari</c> tablosuna yazar.
///
/// <para><b>Neden sarmalayıcı, neden üreticinin İÇİ değil:</b> üreticiler saf ve testli static
/// sınıflar; içlerine log yazımı koymak (a) her birine aynı kodu kopyalar, (b) üretici bir hata
/// fırlattığında log satırının yazılmasını garanti etmez. Sarmalayıcı ölçümü tek yerde tutar ve
/// üretici kodunu HİÇ değiştirmez — mevcut üretici testleri aynen geçerli kalır.</para>
///
/// <para><b>Neden ham SQL, neden <c>db.Add</c> değil:</b> üretici hata fırlattığında
/// <paramref name="db"/>'nin değişiklik izleyicisinde yarım kalmış varlıklar durabilir; log
/// satırını EF ile eklemek <c>SaveChanges</c> sırasında O YARIM İŞİ de yazmaya kalkardı. Parametreli
/// ham INSERT izleyiciye hiç dokunmaz. RLS aynı bağlantıdaki <c>app.tenant_id</c> GUC'u üzerinden
/// yine uygulanır (tenant sızıntısı yok).</para>
///
/// <para><b>Log yazımı işi BOZMAZ:</b> log INSERT'i başarısız olursa (ör. üreticinin hatası
/// transaction'ı iptal etmişse) hata yutulur — üreticinin kendi sonucu/hatası olduğu gibi
/// çağırana geçer.</para>
/// </summary>
public static class JobCalismaKaydedici
{
    // Mevcut metrik etiketleriyle AYNI sözlük (RacarMetrics.JobFailed) — iki yerde iki ad olmasın.
    public const string VadeBildirim = "vade-bildirim";
    public const string FiloBildirim = "filo-bildirim";
    public const string DonemFatura = "donem-fatura";

    /// <summary>
    /// <paramref name="is"/>'i çalıştırır, süresini ölçer ve sonucu (başarı VE hata) loglar.
    /// Üreticinin dönüş değeri aynen geri verilir; fırlattığı hata aynen yeniden fırlatılır.
    /// </summary>
    /// <param name="sayi">Dönüş değerinden "kaç kayıt üretildi" çıkaran seçici (opsiyonel).</param>
    /// <param name="ozet">Başarı durumunda yazılacak kısa özet (opsiyonel).</param>
    public static async Task<T> CalistirAsync<T>(
        AppDbContext db, Guid tenantId, string jobAdi, Func<Task<T>> @is,
        Func<T, int?>? sayi = null, Func<T, string?>? ozet = null, CancellationToken ct = default)
    {
        var bas = DateTimeOffset.UtcNow;
        try
        {
            var sonuc = await @is();
            await YazAsync(db, tenantId, jobAdi, bas, basarili: true,
                sayi?.Invoke(sonuc), Kisalt(ozet?.Invoke(sonuc)), ct);
            return sonuc;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // kapanış iptali hata değil — loglanmaz
        }
        catch (Exception ex)
        {
            await YazAsync(db, tenantId, jobAdi, bas, basarili: false, null, Kisalt(ex.Message), ct);
            throw;
        }
    }

    /// <summary>Tek log satırı yazar. Hata YUTULUR (bkz. sınıf özeti).</summary>
    public static async Task YazAsync(
        AppDbContext db, Guid tenantId, string jobAdi, DateTimeOffset baslangic,
        bool basarili, int? sonucSayisi, string? detay, CancellationToken ct = default)
    {
        try
        {
            var id = Guid.NewGuid();
            var bitis = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO "JobCalismaLoglari"
                     ("Id", "TenantId", "JobAdi", "BaslangicUtc", "BitisUtc", "Basarili", "SonucSayisi", "Detay")
                 VALUES ({id}, {tenantId}, {jobAdi}, {baslangic}, {bitis}, {basarili}, {sonucSayisi}, {detay})
                 """, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Günlük yazımı ASLA işin kendisini düşürmez. (Tipik sebep: üreticinin hatası
            // transaction'ı iptal etmiştir → bu INSERT de reddedilir.)
        }
    }

    /// <summary>Detay kolonu 512 karakter — uzun istisna mesajı yazımı düşürmesin.</summary>
    private static string? Kisalt(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Length <= 512 ? s : s[..509] + "...");
}
