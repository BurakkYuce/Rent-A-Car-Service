using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// İçe aktarılmış tek bir HGS geçişi (PTT HGS kurumsal / banka dökümü). İlk aşamada kaynak DOSYADIR; HGS API'si
/// geldiğinde aynı tabloya aynı doğal anahtarla yazar (<see cref="Kaynak"/>).
///
/// <para><b>Doğal anahtar</b> (unique index, kiracı başına): <see cref="Plaka"/> + <see cref="GecisZamani"/> +
/// <see cref="IstasyonAnahtari"/> + <see cref="Tutar"/>. Aynı dosyanın (ya da çakışan iki dökümün) ikinci kez içe
/// aktarılması yeni satır üretmez.</para>
///
/// <para><b>Eşleştirme:</b> geçiş anı, aracın bir kirasının teslim (BasTar) ≤ an &lt; dönüş (GercekDonusTar; açık
/// kirada sınırsız) aralığındaysa <see cref="RentalId"/> dolar ve durum <see cref="HgsCrossingStatus.Eslesti"/>
/// olur; yoksa <see cref="HgsCrossingStatus.KiraDisi"/> (firma gideri).</para>
///
/// <para><b>Yansıtma</b> mevcut HGS deseniyle yapılır: Borç Cari / Alacak Gelir, <c>SourceType = "Hgs"</c>,
/// <c>SourceId = Id</c> — defterdeki kısmi unique index (<c>…SourceType, SourceId, Direction</c>, yalnız 'Hgs')
/// bir geçişin ikinci kez borçlandırılmasını yapısal olarak imkânsız kılar. Yansıtılmış satır DB trigger'ıyla
/// değişmez.</para>
/// </summary>
public class HgsCrossing : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Plaka eşleştirme anahtarı (büyük harf, boşluksuz, tiresiz).</summary>
    public string Plaka { get; set; } = string.Empty;

    public string? EtiketNo { get; set; }
    public string? AracSinifi { get; set; }
    public string? GirisIstasyonu { get; set; }
    public string? CikisIstasyonu { get; set; }

    /// <summary>Doğal anahtarın istasyon bileşeni: giriş + çıkış, Türkçe katlanmış büyük harf ("" olabilir).</summary>
    public string IstasyonAnahtari { get; set; } = string.Empty;

    /// <summary>Yola giriş anı (UTC), dosyada varsa.</summary>
    public DateTimeOffset? GirisZamani { get; set; }

    /// <summary>Ücretin tahakkuk ettiği an (çıkış; tek noktalı geçişte geçiş anı), UTC. Eşleştirme bu ana bakar.</summary>
    public DateTimeOffset GecisZamani { get; set; }

    /// <summary>Geçiş ücreti (TRY).</summary>
    public decimal Tutar { get; set; }

    public HgsCrossingStatus Durum { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? RentalId { get; set; }

    /// <summary>
    /// Durumun gerekçesi (Belirsiz/KiraDisi'de): "olası mükerrer", "birden çok kira", "kira teslim edilmemiş",
    /// "plaka filoda yok". İçe aktarma ve yeniden eşleştirme her seferinde yeniden yazar.
    /// </summary>
    public string? EslesmeNotu { get; set; }

    /// <summary>Olası mükerrer satır FinanceWrite'lı kullanıcı tarafından "ayrı geçiş" onaylandıysa onay anı (UTC):
    /// satır iki mükerrer kuralından kalıcı olarak muaftır. Kim onayladı: denetim kaydı.</summary>
    public DateTimeOffset? OnayZamani { get; set; }

    // ---- yansıtma (yalnız Durum = Yansitildi iken dolu) ----
    public decimal? HizmetOrani { get; set; }
    public decimal? YansitilanTutar { get; set; }
    public DateTimeOffset? YansitmaZamani { get; set; }
    public Guid? YansitilanCariId { get; set; }

    // ---- köken ----
    /// <summary>Aynı yüklemede gelen satırları gruplar.</summary>
    public Guid IceAktarimId { get; set; }

    /// <summary>"Dosya" (ilk aşama) ya da ileride "Api".</summary>
    public string Kaynak { get; set; } = "Dosya";

    public string? DosyaAdi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
