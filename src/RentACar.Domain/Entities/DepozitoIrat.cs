using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Depozito İRAT kaydı (FAZ 1.2 — kullanıcı kararı): müşteriye iade edilmeyip GELİR kalan depozito.
/// Defter çifti (Borç Depozito / Alacak Gelir, SourceType="DepozitoIrat") ile AYNI transaction'da yazılır;
/// bu tablo atıf/iz kaydıdır — RentalId üzerinden kârlılık/araç-karnesi gelir atfına girer
/// (SourceId = bu kaydın Id'si). Mali iz olduğundan DEĞİŞMEZDİR (rc_prevent_mutation trigger).
/// Tutar/Currency/Kur belge (native) değerleridir; defter katkısı Tutar×Kur (baz TL).
/// </summary>
public class DepozitoIrat : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid CariId { get; set; }

    /// <summary>Opsiyonel kira bağı — verilirse gelir o kiranın aracına atfedilir (karne/Karlilik).</summary>
    public Guid? RentalId { get; set; }

    public decimal Tutar { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public DateTimeOffset Tarih { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
