using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri taksiti (FAZ-66; canlı <c>kredi_takip_listesi.aspx</c>). Müşteriye taksitli satış/kredi
/// ile araç verme sürecinin TAKİBİ.
///
/// <para><b>PARA YAZMAZ — DEFTERE DOKUNMAZ.</b> Bu bir takip kaydıdır, mali belge değildir:
/// tahsilat yapıldığında defteri hareket ettiren yer Kasa/Banka tahsilat akışıdır. Buradaki
/// "Ödendi" işareti yalnız takip amaçlıdır ve cari bakiyeyi DEĞİŞTİRMEZ. İki akışı birbirine
/// bağlamak (taksit işaretleyince defter yazmak) çift-kayıt üretirdi.</para>
///
/// <para><b><see cref="AracKredi"/> İLE KARIŞTIRILMAMALI:</b> o, şirketin BANKAYA olan borcudur
/// (araç alımı finansmanı); bu ise MÜŞTERİNİN ŞİRKETE olan borcudur. İki farklı taraf, iki farklı
/// yön — aynı tabloda birleştirmek kavramsal karıştırma olurdu.</para>
/// </summary>
public class MusteriTaksit : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid CariId { get; set; }

    /// <summary>Hangi araç (opsiyonel — plakasız genel taksit planı da olabilir).</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>Taksitli satış kaydı bağı (opsiyonel).</summary>
    public Guid? VehicleSaleId { get; set; }

    /// <summary>Plan içindeki sıra (1'den başlar). Tek satırlık kayıtta 1.</summary>
    public int Sira { get; set; } = 1;

    public DateTimeOffset Vade { get; set; }
    public decimal TaksitTutari { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public TaksitDurum Durum { get; set; } = TaksitDurum.Bekliyor;
    /// <summary>Ödendi işaretlendiği an (takip bilgisi — defter tarihi DEĞİLDİR).</summary>
    public DateTimeOffset? OdemeTarihi { get; set; }

    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Taksit tutarının baz para karşılığı (bilgi — deftere yazılmaz).</summary>
    public decimal TutarBaz => TaksitTutari * Kur;

    /// <summary>
    /// Gecikme TÜRETİLİR: vadesi geçmiş ve ödenmemiş. Kolona yazılsaydı gece yarısı bayatlar,
    /// bir job'a bağımlı hâle gelir ve güncellenmediğinde rapor yanlış gösterirdi.
    /// </summary>
    public bool Gecikti => Durum != TaksitDurum.Odendi && Vade.UtcDateTime.Date < DateTimeOffset.UtcNow.UtcDateTime.Date;
}
