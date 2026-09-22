using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Arka plan üretici koşularını <c>JobCalismaLoglari</c> tablosuna yazar.
///
/// <para><b>Neden sarmalayıcı, neden üreticinin İÇİ değil:</b> üreticiler saf ve testli static
/// sınıflar; içlerine log yazımı koymak (a) her birine aynı kodu kopyalar, (b) üretici bir hata
/// fırlattığında log satırının yazılmasını garanti etmez. Sarmalayıcı ölçümü tek yerde tutar ve
/// üretici kodunu HİÇ değiştirmez — mevcut üretici testleri aynen geçerli kalır.</para>
///
/// <para><b>Neden üreticinin bağlantısından AYRI bir bağlantı:</b> satır, üreticinin
/// <paramref name="db"/>'sinin bağlantısıyla yazılırsa üreticinin bıraktığı durumu miras alır.
/// Canlıda görüldü (#265): Npgsql parametre yazarken patlayınca (ör. +03:00 ofsetli timestamptz)
/// bağlantı kırılıyor, EF bir sonraki komutta havuzdan TAZE bir bağlantı açıyor ve o bağlantıda
/// <c>app.tenant_id</c> yok → hata satırı RLS'e (42501) takılıyor ve yutuluyordu; hata beş hafta
/// boyunca koşu günlüğünde hiç görünmedi. Artık satır kendi kısa bağlantısında, kendi
/// transaction'ında ve transaction-yerel tenant GUC'u ile yazılır: üreticinin kırık bağlantısı,
/// iptal edilmiş transaction'ı veya izleyicideki yarım varlıkları satırı etkileyemez. Aynı
/// <c>racar_app</c> bağlantı dizesi kullanılır, RLS aynen uygulanır (tenant sızıntısı yok).</para>
///
/// <para><b>Log yazımı işi BOZMAZ:</b> log INSERT'i yine de başarısız olursa hata işi düşürmez —
/// üreticinin kendi sonucu/hatası olduğu gibi çağırana geçer — ama artık SESSİZ de değildir:
/// <c>log</c> verilmişse Warning olarak yazılır.</para>
/// </summary>
public static class JobCalismaKaydedici
{
    // Mevcut metrik etiketleriyle AYNI sözlük (RacarMetrics.JobFailed) — iki yerde iki ad olmasın.
    public const string VadeBildirim = "vade-bildirim";
    public const string FiloBildirim = "filo-bildirim";
    public const string MusteriBildirim = "musteri-bildirim";
    public const string DonemFatura = "donem-fatura";
    /// <summary>WhatsApp günlük özeti — koşu günlüğüne yazılmaz (kendi gönderim tablosu var),
    /// yalnız hata logu ve metrik etiketi olarak kullanılır.</summary>
    public const string WhatsAppOzet = "whatsapp-ozet";

    /// <summary>
    /// <paramref name="is"/>'i çalıştırır, süresini ölçer ve sonucu (başarı VE hata) loglar.
    /// Üreticinin dönüş değeri aynen geri verilir; fırlattığı hata aynen yeniden fırlatılır.
    /// </summary>
    /// <param name="sayi">Dönüş değerinden "kaç kayıt üretildi" çıkaran seçici (opsiyonel).</param>
    /// <param name="ozet">Başarı durumunda yazılacak kısa özet (opsiyonel).</param>
    /// <param name="log">Günlük satırı yazılamazsa Warning buraya düşer (üretim çağıranları verir).</param>
    public static async Task<T> CalistirAsync<T>(
        AppDbContext db, Guid tenantId, string jobAdi, Func<Task<T>> @is,
        Func<T, int?>? sayi = null, Func<T, string?>? ozet = null, CancellationToken ct = default,
        ILogger? log = null)
    {
        var bas = DateTimeOffset.UtcNow;
        try
        {
            var sonuc = await @is();
            await YazAsync(db, tenantId, jobAdi, bas, basarili: true,
                sayi?.Invoke(sonuc), Kisalt(ozet?.Invoke(sonuc)), ct, log);
            return sonuc;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // kapanış iptali hata değil — loglanmaz
        }
        catch (Exception ex)
        {
            await YazAsync(db, tenantId, jobAdi, bas, basarili: false, null, Kisalt(ex.Message), ct, log);
            throw;
        }
    }

    /// <summary>
    /// Tek log satırı yazar — üreticinin bağlantısından BAĞIMSIZ kısa bir bağlantıda (bkz. sınıf özeti).
    /// <paramref name="db"/> yalnız bağlantı dizesi için kullanılır. Hata işi düşürmez; Warning olarak loglanır.
    /// </summary>
    public static async Task YazAsync(
        AppDbContext db, Guid tenantId, string jobAdi, DateTimeOffset baslangic,
        bool basarili, int? sonucSayisi, string? detay, CancellationToken ct = default,
        ILogger? log = null)
    {
        try
        {
            var cs = db.Database.GetConnectionString()
                ?? throw new InvalidOperationException("Context'in bağlantı dizesi yok.");
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Transaction-YEREL GUC (is_local=true): commit/rollback ile biter, havuza dönen
            // bağlantıda tenant kalıntısı bırakmaz.
            await using (var guc = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx))
            {
                guc.Parameters.AddWithValue("t", tenantId.ToString());
                await guc.ExecuteNonQueryAsync(ct);
            }

            await using (var ins = new NpgsqlCommand(
                """
                INSERT INTO "JobCalismaLoglari"
                    ("Id", "TenantId", "JobAdi", "BaslangicUtc", "BitisUtc", "Basarili", "SonucSayisi", "Detay")
                VALUES (@id, @tenant, @ad, @bas, @bit, @ok, @sayi, @detay)
                """, conn, tx))
            {
                ins.Parameters.AddWithValue("id", Guid.NewGuid());
                ins.Parameters.AddWithValue("tenant", tenantId);
                ins.Parameters.AddWithValue("ad", jobAdi);
                // timestamptz yalnız Offset=0 kabul eder (#265 dersi) — çağıran ofsetli verse de UTC'ye çek.
                ins.Parameters.AddWithValue("bas", baslangic.ToUniversalTime());
                ins.Parameters.AddWithValue("bit", DateTimeOffset.UtcNow);
                ins.Parameters.AddWithValue("ok", basarili);
                ins.Parameters.Add(new NpgsqlParameter("sayi", NpgsqlDbType.Integer) { Value = (object?)sonucSayisi ?? DBNull.Value });
                ins.Parameters.Add(new NpgsqlParameter("detay", NpgsqlDbType.Varchar) { Value = (object?)detay ?? DBNull.Value });
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Günlük yazımı ASLA işin kendisini düşürmez — ama sessizce de kaybolmaz.
            log?.LogWarning(ex, "Koşu günlüğü yazılamadı: iş {JobAdi}, tenant {Tenant}, başarılı={Basarili}.",
                jobAdi, tenantId, basarili);
        }
    }

    /// <summary>Detay kolonu 512 karakter — uzun istisna mesajı yazımı düşürmesin.</summary>
    private static string? Kisalt(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Length <= 512 ? s : s[..509] + "...");
}
