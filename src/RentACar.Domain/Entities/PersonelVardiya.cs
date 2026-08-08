using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Personel vardiyası (FAZ-45; canlı <c>personel_calisma_grafigi.aspx</c> karşılığı).
/// Bir personelin BELİRLİ BİR GÜNDE, BELİRLİ BİR ŞUBEDE çalıştığı saat aralığı.
///
/// <para><b>Şube, vardiyanın kendi alanıdır — personelin şubesi DEĞİL.</b> Personel master'ında da
/// bir <c>Sube</c> var ama o "kadro şubesi"dir; vardiya "o gün fiilen nerede çalıştı"yı tutar
/// (destek/rotasyon gerçek bir senaryo). Şube kapsamı (Operatör) VARDİYANIN şubesine bakar:
/// Şube A operatörü, kadrosu B'de olan ama o gün A'da çalışan personeli görmelidir.</para>
///
/// <para><b>Gece vardiyası desteklenir:</b> <see cref="BitisSaat"/> &lt; <see cref="BaslangicSaat"/>
/// ise vardiya ERTESİ GÜN biter (22:00–06:00). Süre ve çakışma hesapları bunu hesaba katar;
/// "bitiş başlangıçtan sonra olmalı" diye reddetseydik havalimanı ofisi gece vardiyası
/// girilemezdi. Yalnız SIFIR uzunluk (iki saat eşit) reddedilir.</para>
///
/// <para>Para/defter TAŞIMAZ — mesai ücreti hesabı bu fazın kapsamı dışında (bilinçli).</para>
/// </summary>
public class PersonelVardiya : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid PersonelId { get; set; }

    /// <summary>Vardiyanın BAŞLADIĞI takvim günü (gece vardiyası ertesi güne taşar).</summary>
    public DateOnly Tarih { get; set; }

    public TimeOnly BaslangicSaat { get; set; }
    public TimeOnly BitisSaat { get; set; }

    /// <summary>Vardiyanın çalışıldığı şube (serbest metin; Branch master'ıyla additive tutarlı).</summary>
    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK — metinden türetilir (BranchFkInterceptor)

    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }

    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Vardiya uzunluğu (dakika) — gece vardiyasında gün aşımı eklenir. 0 asla dönmez.</summary>
    public int SureDk => VardiyaZaman.SureDk(BaslangicSaat, BitisSaat);
}

/// <summary>
/// Vardiya zaman aritmetiği. Domain'de saf fonksiyon olarak durur ki hem doğrulama (çakışma) hem
/// rapor (toplam saat) AYNI kuralı kullansın — iki yerde ayrı yazılsa gece vardiyasında ayrışırdı.
/// </summary>
public static class VardiyaZaman
{
    public const int GunDk = 24 * 60;

    /// <summary>Gün-içi dakika (0..1439).</summary>
    public static int Dk(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>Süre; bitiş &lt;= başlangıç ise ertesi güne taşar (gece vardiyası).</summary>
    public static int SureDk(TimeOnly bas, TimeOnly bit)
    {
        var d = Dk(bit) - Dk(bas);
        return d > 0 ? d : d + GunDk;
    }

    /// <summary>Vardiyanın MUTLAK aralığı (epoch-gün × 1440 tabanında) — çakışma karşılaştırması için.</summary>
    public static (long Bas, long Bit) Aralik(DateOnly tarih, TimeOnly bas, TimeOnly bit)
    {
        var b = (long)tarih.DayNumber * GunDk + Dk(bas);
        return (b, b + SureDk(bas, bit));
    }
}
