using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Liste sözleşmesinin (F1.3) EF Core uygulaması: filtrelenmiş sorgu + <see cref="ListeIstegi"/> +
/// <see cref="SortFieldMap{T}"/> → <see cref="Sayfa{T}"/>. Infrastructure'da yaşar çünkü
/// <c>CountAsync</c>/<c>ToListAsync</c> EF'e bağlıdır; Application EF'e bağımlı değildir (sözleşme
/// tipleri ve beyaz liste orada, sağlayıcıdan bağımsız).
///
/// <para>Sıra: (1) filtreli sorguda <c>COUNT</c> (sıralamasız — gereksiz ORDER BY yok),
/// (2) beyaz liste sıralaması + eşitlik bozucu, (3) <c>OFFSET/LIMIT</c>. Atlanacak kayıt toplamı
/// aşıyorsa ikinci sorgu HİÇ atılmaz (boş sayfa) — bu aynı zamanda çok büyük sayfa numarasında
/// <c>int</c> taşmasını da engeller (<see cref="ListeIstegi.Atla"/> <c>long</c>).</para>
///
/// <para>İki sorgu aynı anlık görüntüde değildir: arada eklenen/silinen satır <c>Toplam</c> ile
/// <c>Kayitlar</c> arasında ±1 fark yaratabilir — liste ekranı için kabul edilebilir, para toplamı
/// için bu yardımcı KULLANILMAZ.</para>
/// </summary>
public static class Sayfalama
{
    /// <summary>Entity'lerin kendisini sayfalar.</summary>
    public static Task<Sayfa<T>> SayfalaAsync<T>(
        this IQueryable<T> sorgu, ListeIstegi istek, SortFieldMap<T> harita, CancellationToken ct = default)
        => sorgu.SayfalaAsync(istek, harita, x => x, ct);

    /// <summary>
    /// Sıralar, sayfalar ve SONRA SQL'de izdüşüm alır (<paramref name="secici"/> EF tarafından
    /// çevrilmeli). Sıralama entity alanları üzerinden yapıldığı için DTO'da olmayan bir alana
    /// göre de sıralanabilir.
    /// </summary>
    public static async Task<Sayfa<TSonuc>> SayfalaAsync<T, TSonuc>(
        this IQueryable<T> sorgu, ListeIstegi istek, SortFieldMap<T> harita,
        Expression<Func<T, TSonuc>> secici, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sorgu);
        ArgumentNullException.ThrowIfNull(istek);
        ArgumentNullException.ThrowIfNull(harita);
        ArgumentNullException.ThrowIfNull(secici);

        // Geçersiz sıralama alanı COUNT'tan ÖNCE reddedilsin (boşa sorgu atılmasın).
        var sirali = harita.Apply(sorgu, istek.Sirala);

        var toplam = await sorgu.CountAsync(ct);
        if (istek.Atla >= toplam)
            return new Sayfa<TSonuc>([], toplam, istek.Sayfa, istek.Boyut);

        var kayitlar = await sirali
            .Skip((int)istek.Atla)
            .Take(istek.Boyut)
            .Select(secici)
            .ToListAsync(ct);
        return new Sayfa<TSonuc>(kayitlar, toplam, istek.Sayfa, istek.Boyut);
    }
}
