using RentACar.Domain.Entities;

namespace RentACar.Application.WebSite;

/// <summary>PR-13: ilan + üye araçları + özellikleri tek okumada döner (liste ekranı ve yayın kapısı
/// üçünü birden ister; ayrı ayrı çekmek N+1 üretirdi).</summary>
public sealed record WebIlanDetay(
    WebIlan Ilan, IReadOnlyList<Vehicle> Araclar, IReadOnlyList<WebIlanOzellik> Ozellikler);

public interface IWebIlanRepository
{
    Task<IReadOnlyList<WebIlanDetay>> ListAsync(CancellationToken ct = default);

    Task<WebIlanDetay?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>PR-14: halka açık adres çözümü (<c>/araclar/{slug}</c>).</summary>
    Task<WebIlanDetay?> FindBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>PR-14: kullanılmakta olan tüm slug'lar — sihirbaz otomatik son-ek verirken
    /// ("…-2", "…-3") çakışma kontrolü için. Tenant kapsamı query filter + RLS ile otomatik.</summary>
    Task<IReadOnlyList<string>> ListSluglarAsync(CancellationToken ct = default);

    /// <summary>Aynı eşleşme anahtarına sahip mevcut ilanlar (operatörün İKİZ ilan yaratmasını
    /// engellemek ve sonradan alınan aracı doğru ilana katmak için).</summary>
    Task<IReadOnlyList<WebIlan>> FindByAnahtarAsync(string anahtar, CancellationToken ct = default);

    /// <summary>
    /// Sihirbaz adım-1: ilan(lar)ı ve araç üyeliklerini TEK transaction'da yazar. Yarım kalırsa
    /// filo yarı-ilanlı kalmamalı (12 araçtan 5'i ilana bağlı, 7'si boşta = sessiz tutarsızlık).
    ///
    /// <paramref name="gruplar"/>'daki <c>Yeni=false</c> satırlar MEVCUT bir ilana katılımdır —
    /// ilan INSERT EDİLMEZ, yalnız araç üyelikleri yazılır (ikiz-ilan koruması bu yolu kullanır).
    /// </summary>
    Task CreateWithUyelikAsync(IReadOnlyList<(WebIlan Ilan, bool Yeni, IReadOnlyList<Guid> AracIdler)> gruplar,
        CancellationToken ct = default);

    /// <summary>Adım-2/3: ilanı günceller (fiyat ya da durum). Yoksa false.</summary>
    Task<bool> UpdateAsync(Guid id, Action<WebIlan> apply, CancellationToken ct = default);

    /// <summary>Adım-2 "ayrı" modda: kardeş ilanlara (aynı anahtar, aynı oturumda yaratılmış taslaklar)
    /// aynı fiyatı uygular. Dönen: etkilenen ilan sayısı.</summary>
    Task<int> KardeslereFiyatKopyalaAsync(Guid kaynakIlanId, CancellationToken ct = default);

    /// <summary>Adım-3: özellik satırlarını TAMAMEN değiştirir (sil + yaz, tek transaction).</summary>
    Task ReplaceOzelliklerAsync(Guid ilanId, IReadOnlyList<WebIlanOzellik> satirlar, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Henüz hiçbir ilana bağlanmamış araçlar (sihirbazın adım-1 havuzu + "yayınlanmamış N araç"
    /// tanılaması). Şube kapsamı ÇAĞIRANDA uygulanır.</summary>
    Task<IReadOnlyList<Vehicle>> ListIlansizAraclarAsync(CancellationToken ct = default);
}
