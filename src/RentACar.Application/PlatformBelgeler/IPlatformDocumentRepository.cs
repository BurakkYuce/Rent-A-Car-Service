using RentACar.Domain.Entities;

namespace RentACar.Application.PlatformBelgeler;

/// <summary>Liste satırı — <b>PDF içeriği YOK</b> (üstveri-only; blob liste sorgusuna girmez).</summary>
public sealed record FirmaBelgeSatiri(
    Guid Id, string Baslik, string? Aciklama, int Surum, DateTimeOffset Guncelleme, long Boyut, bool Yeni);

/// <summary>İndirme içeriği + ETag'in sürüm bileşeni.</summary>
public sealed record BelgeIcerik(byte[] Bytes, string DosyaAdi, int Surum);

/// <summary>
/// PR-B — tenant tarafı belge okuma. <b>SALT-OKUR</b>: yazma yalnız platform konsolundadır.
///
/// <para><see cref="PlatformBelge"/> bir PLATFORM tablosudur (RLS yok, merkezi query filter yok) →
/// erişim kontrolü bu arayüzün ARDINDA, uygulama katmanında kurulur. Metotlar tenant'ı ve rolü
/// PARAMETRE olarak alır (<c>TenantDomainRepository</c> deseni: repo kimliği bilmez, karar servisin).</para>
/// </summary>
public interface IPlatformDocumentRepository
{
    /// <summary>Bu tenant'ın görebileceği YAYINDA belgeler. <paramref name="isManager"/> false ise
    /// <c>YalnizYoneticiler</c> belgeler DIŞARIDA bırakılır. Blob SEÇİLMEZ.</summary>
    Task<IReadOnlyList<FirmaBelgeSatiri>> ListAsync(Guid tenantId, bool isManager, CancellationToken ct = default);

    /// <summary>
    /// Tek belgeyi İÇERİĞİYLE getirir — <b>ancak dört koşul birden sağlanırsa</b>:
    /// belge var · <c>Durum=Yayinda</c> · (global veya bu tenant'a hedefli) · (herkese açık veya
    /// <paramref name="isManager"/>). Aksi halde <c>null</c> — "yok" ile "yetkisiz" AYIRT EDİLMEZ
    /// (varlık bilgisi de sızmasın).
    /// </summary>
    Task<BelgeIcerik?> DownloadAsync(Guid documentId, Guid tenantId, bool isManager, CancellationToken ct = default);
}
