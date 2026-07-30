using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// PR-C — sözleşme paylaşım linki yönetimi (oluştur / yeni sürüm / iptal / durum).
///
/// <para><b>PDF baytını ÜRETMEZ, ÇAĞIRANDAN ALIR.</b> Sözleşme render'ı QuestPDF'e bağlı
/// (<c>PdfExportService</c>, RentACar.Web) ve o bağımlılık Application katmanına giremez. Web ucu
/// <c>SozlesmeService.GetAsync</c> → <c>PdfExportService.Contract</c> zincirini kurar, baytı buraya
/// verir. Katman yönü korunur, render tek kaynak kalır.</para>
///
/// <para><b>Yetki:</b> <see cref="Permission.OperationsWrite"/> — sözleşme paylaşmak operasyonel bir
/// iştir. Yeni bir Permission değeri eklenmedi. Web ucu ayrıca aynı izni ister (çift savunma).</para>
///
/// <para><b>"Gönderildi" DİYE BİR DURUM YOK.</b> Personel linki WhatsApp/Gmail ile paylaşır; bunlar
/// gönderimi kanıtlamaz. Kaydın söylediği tek şey "paylaşım linki oluşturuldu"; gerçek kanıt erişim
/// sayacıdır.</para>
/// </summary>
public sealed class SozlesmePaylasimService(ISozlesmePaylasimRepository repository, ICurrentUser currentUser)
{
    /// <summary>Aktif link durumu (yoksa null). Panelin okuduğu tek yer.</summary>
    public Task<PaylasimDurum?> DurumAsync(Guid rentalId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.AktifAsync(rentalId, ct);
    }

    /// <summary>
    /// Paylaş: aktif link varsa <b>aynısını</b> döner (yeni snapshot üretilmez). Böylece aynı
    /// sözleşme için ikinci kez paylaşmak müşterinin elindeki adresi bozmaz.
    /// </summary>
    public Task<PaylasimDurum> PaylasAsync(Guid rentalId, string sozlesmeNo, byte[] pdf,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        Dogrula(sozlesmeNo, pdf);
        return repository.OlusturAsync(rentalId, sozlesmeNo, GuvenliToken.Uret(), pdf, ct);
    }

    /// <summary>Yeni sürüm: eski link iptal + yeni token + tazelenmiş anlık görüntü.</summary>
    public Task<PaylasimDurum> YeniSurumAsync(Guid rentalId, string sozlesmeNo, byte[] pdf,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        Dogrula(sozlesmeNo, pdf);
        return repository.YeniSurumAsync(rentalId, sozlesmeNo, GuvenliToken.Uret(), pdf, ct);
    }

    /// <summary>İptal: adres 404 olur, PDF silinir, erişim geçmişi kalır.</summary>
    public Task<bool> IptalEtAsync(Guid rentalId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.IptalAsync(rentalId, ct);
    }

    /// <summary>PDF gerçekten üretilmiş mi — boş bayt sessizce saklanıp müşteriye bozuk dosya gitmesin.</summary>
    private static void Dogrula(string sozlesmeNo, byte[] pdf)
    {
        if (string.IsNullOrWhiteSpace(sozlesmeNo))
            throw new ValidationException("Sözleşme numarası olmadan paylaşım linki üretilemez.");
        if (!PdfValidation.GecerliPdf(pdf))
            throw new ValidationException("Sözleşme PDF'i üretilemedi.");
    }
}
