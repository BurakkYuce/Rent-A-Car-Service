using RentACar.Domain.Entities;

namespace RentACar.Application.FirmaDokumanlar;

/// <summary>
/// Liste satırı — <b>bytea TAŞIMAZ</b>. Sebep tek cümlede: 10 belgelik bir liste sayfası 10 PDF'i
/// (en kötü 100 MB) Postgres'ten belleğe çeker ve hiçbirini kullanmazdı. Aynı ders blog kapağında
/// (<c>BlogListItem</c>) ve platform belgelerinde (<c>FirmaBelgeSatiri</c>) de kayıtlı.
/// </summary>
public sealed record FirmaDokumanSatiri(
    Guid Id, string Baslik, string? Aciklama, string DosyaAdi, long Boyut, int Sira,
    string? YukleyenKullanici, DateTimeOffset CreatedAtUtc);

/// <summary>İndirme yükü — TEK bytea'ya dokunan yol. <see cref="GuncellemeUtc"/> ETag üretimi için.</summary>
public sealed record FirmaDokumanIcerik(
    byte[] Bytes, string ContentType, string DosyaAdi, DateTimeOffset GuncellemeUtc);

/// <summary>
/// Firma dokümanı kalıcılığı. TASARIM KURALI: <c>FirmaDokuman.Bytes</c> yalnız
/// <see cref="IndirAsync"/> SELECT'ine girer; listeler DTO projeksiyonudur.
/// </summary>
public interface IFirmaDokumanRepository
{
    /// <summary>Sıra (yuva) numarasına göre artan; bytea taşımaz.</summary>
    Task<IReadOnlyList<FirmaDokumanSatiri>> ListeleAsync(CancellationToken ct = default);

    /// <summary>Tenant'ın mevcut belge sayısı (sınır kontrolü için; bytea okumaz).</summary>
    Task<int> SayAsync(CancellationToken ct = default);

    /// <summary>
    /// Boş yuvayı (1..<paramref name="maxYuva"/>) bulup atar ve kaydeder. Yuva kalmamışsa
    /// <c>ValidationException</c>. Eşzamanlı iki yükleme aynı yuvayı seçerse DB unique index
    /// ihlali yine <c>ValidationException</c>'a çevrilir — 11. satır YAZILAMAZ.
    /// </summary>
    Task<Guid> EkleAsync(FirmaDokuman dokuman, int maxYuva, CancellationToken ct = default);

    Task<bool> SilAsync(Guid id, CancellationToken ct = default);

    /// <summary>Bytea'ya dokunan TEK metot. Başka tenant'ın belgesi → RLS + query filter yüzünden null.</summary>
    Task<FirmaDokumanIcerik?> IndirAsync(Guid id, CancellationToken ct = default);
}
