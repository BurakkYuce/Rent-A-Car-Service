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
public static class Paging
{
    /// <summary>Entity'lerin kendisini sayfalar.</summary>
    public static Task<Sayfa<T>> PaginateAsync<T>(
        this IQueryable<T> query, ListeIstegi request, SortFieldMap<T> map, CancellationToken ct = default)
        => query.PaginateAsync(request, map, x => x, ct);

    /// <summary>
    /// Sıralar, sayfalar ve SONRA SQL'de izdüşüm alır (<paramref name="picker"/> EF tarafından
    /// çevrilmeli). Sıralama entity alanları üzerinden yapıldığı için DTO'da olmayan bir alana
    /// göre de sıralanabilir.
    /// </summary>
    public static async Task<Sayfa<TSonuc>> PaginateAsync<T, TSonuc>(
        this IQueryable<T> query, ListeIstegi request, SortFieldMap<T> map,
        Expression<Func<T, TSonuc>> picker, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(picker);

        // Geçersiz sıralama alanı COUNT'tan ÖNCE reddedilsin (boşa sorgu atılmasın).
        var sorted = map.Apply(query, request.Sirala);

        var total = await query.CountAsync(ct);
        if (request.Atla >= total)
            return new Sayfa<TSonuc>([], total, request.Sayfa, request.Boyut);

        var records = await sorted
            .Skip((int)request.Atla)
            .Take(request.Boyut)
            .Select(picker)
            .ToListAsync(ct);
        return new Sayfa<TSonuc>(records, total, request.Sayfa, request.Boyut);
    }
}
