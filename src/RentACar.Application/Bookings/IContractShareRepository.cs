namespace RentACar.Application.Bookings;

/// <summary>
/// Bir kiranın aktif paylaşım linkinin durumu. <b>Token dışında kişisel veri taşımaz</b> — panelde
/// gösterilen her şey burada.
/// </summary>
/// <param name="Token">Linkin adres bileşeni (32-byte CSPRNG → Base64Url).</param>
/// <param name="ErisimSayisi">Kaç kez açıldı. IP TUTULMUYOR — "müşteri açmadı diyor" sorusuna sayaç yeter.</param>
/// <param name="Bayat">
/// Kira, anlık görüntü alındıktan SONRA güncellenmiş mi. Bu bayrak olmadan personel Paylaş'a basar,
/// müşteriye eski nüsha gider ve kimse fark etmez. Otomatik yenileme bilinçli YOK: müşterinin
/// elindeki belge sessizce değişmemeli — karar personelin ("Yeni sürüm oluştur").
/// </param>
public sealed record PaylasimDurum(
    string Token,
    int ErisimSayisi,
    DateTimeOffset? SonErisimUtc,
    DateTimeOffset OlusturmaUtc,
    DateTimeOffset AnlikGoruntuUtc,
    bool Bayat);

/// <summary>
/// PR-C — paylaşım linki + anlık görüntü kalıcılığı. İKİ tabloya yazar ve bu ayrım zorunludur:
/// <c>PaylasimLinkler</c> platform tablosudur (RLS YOK — anonim token çözümü için),
/// <c>SozlesmePdfler</c> tenant-owned + RLS'dir (kişisel veri PDF'in içinde).
/// </summary>
public interface IContractShareRepository
{
    /// <summary>Aktif (iptal edilmemiş) link — yoksa <c>null</c>.</summary>
    Task<PaylasimDurum?> ActiveAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>
    /// Aktif link + anlık görüntü oluşturur. Aktif link VARSA hiçbir şey üretmez, mevcudu döner
    /// (her Paylaş tıklaması yeni bir PDF saklarsa bytea şişer; ayrıca müşterinin elindeki link
    /// tıklama başına değişirdi).
    /// </summary>
    Task<PaylasimDurum> CreateAsync(Guid rentalId, string contractNo, string token, byte[] pdf,
        CancellationToken ct = default);

    /// <summary>
    /// Yeni sürüm: eski link İPTAL edilir (kayıt durur → sayaç/log kaybolmaz), anlık görüntü
    /// değiştirilir, YENİ token verilir. Eski adres artık 404.
    /// </summary>
    Task<PaylasimDurum> NewVersionAsync(Guid rentalId, string contractNo, string token, byte[] pdf,
        CancellationToken ct = default);

    /// <summary>
    /// İptal: link satırı <b>durur</b> (Iptal=true — erişim geçmişi kanıt), anlık görüntü SİLİNİR
    /// (aksi halde iptal edilmiş her paylaşım bir PDF'i süresiz taşır). Aktif link yoksa <c>false</c>.
    /// </summary>
    Task<bool> CancelAsync(Guid rentalId, CancellationToken ct = default);
}
