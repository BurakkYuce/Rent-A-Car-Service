using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tenant'ın müşteriye giden mesaj şablonu — tür + kanal başına tek kayıt.
///
/// <para>Şablon yoksa gönderim YAPILMAZ (varsayılan metin gömülmez): firma adına müşteriye giden
/// bir mesajın metnini firma görmeden yollamak, yanlış imza/ton/iletişim bilgisi riskidir. Ayarlar
/// ekranı "önerilen metni doldur" butonu sunar, ama yazan yine firmadır.</para>
///
/// <para><b>Yer tutucular:</b> <c>{MusteriAd}</c>, <c>{Firma}</c>, <c>{Plaka}</c>, <c>{Arac}</c>,
/// <c>{CikisTarih}</c>, <c>{DonusTarih}</c>, <c>{CikisOfis}</c>, <c>{Tutar}</c>, <c>{No}</c>.
/// Bilinmeyen yer tutucu metinde OLDUĞU GİBİ kalır (sessizce boşa çevirmek, müşteriye giden
/// mesajda fark edilmeyen boşluklar üretirdi).</para>
/// </summary>
public class MesajSablon : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public MesajTuru Tur { get; set; }
    public MesajKanal Kanal { get; set; }

    /// <summary>E-posta konusu. SMS'te kullanılmaz.</summary>
    public string? Konu { get; set; }

    /// <summary>Mesaj gövdesi (e-postada HTML, SMS'te düz metin).</summary>
    public string Govde { get; set; } = string.Empty;

    /// <summary>Kapalıysa bu tür+kanal için gönderim yapılmaz (metin korunur).</summary>
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
