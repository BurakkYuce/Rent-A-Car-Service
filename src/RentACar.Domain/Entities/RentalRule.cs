using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kiralama kuralı / promosyon-kampanya (canlı TürevRent kiralama_kurallari + kiralama_sartlari +
/// rezsartlar karşılığı): tarih/gün/min-max gün bazlı indirim-promosyon kuralı + kira/rezervasyon şart
/// metni. Şube + kanal (rez kaynağı) + araç grubu kapsamına uygulanır. Tenant-owned + auditable. Saf
/// kural-tanım — deftere kayıt POSTLAMAZ; fiyat motorunda (parite #7) indirim/min-gün girdisidir.
/// <see cref="Kod"/> tenant içinde benzersiz.
/// </summary>
public class RentalRule : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kural kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Kapsam
    /// <summary>Kanal = Rez Kaynağı (boş → tümü).</summary>
    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK (roadmap F1; metin korunur)

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }
    public string? AracGrupKod { get; set; }

    // Gün kısıtları
    public int? MinGun { get; set; }
    public int? MaxGun { get; set; }

    // İndirim / promosyon
    /// <summary>İskonto oranı (%).</summary>
    public decimal? Iskonto { get; set; }
    /// <summary>Hafta sonu (Cmt/Pzr) günlük ücrete ek fark oranı (%) — roadmap G3 (opt-in).</summary>
    public decimal? HaftaSonuFarkOran { get; set; }
    /// <summary>"Sonra Öde" oranı (%).</summary>
    public decimal? SonraOdeOran { get; set; }
    /// <summary>Hediye gün (kampanya: N gün al, M gün öde).</summary>
    public int? HediyeGun { get; set; }
    public bool KampanyaMi { get; set; }
    public string? KampanyaKodu { get; set; }

    /// <summary>Müşteri segmenti kapsamı (FAZ 3.A2) — Customer.Sinif ile Trim + case-insensitive
    /// eşleşir; null = tüm müşteriler. Segment-birebir eşleşen kural fayda kıyasından ÖNCE kazanır
    /// ("Problemli → %0" çiti cömert genel kurala yenilmez). CariId-özel kural bilinçli YOK.</summary>
    public string? MusteriSegment { get; set; }

    // Geçerlilik
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    // ---- FAZ-46 (canlı kiralama_kurallari / kiralama_sartlari) ----
    // DİKKAT: TalepBas/TalepBit, GecerlilikBas/Bit'ten FARKLI bir kavramdır ve onun yerine GEÇMEZ.
    //   Gecerlilik* = kuralın YÜRÜRLÜKTE olduğu aralık (kiralama tarihine bakar).
    //   Talep*       = talebin/rezervasyonun YAPILDIĞI aralık (erken rezervasyon kampanyası).
    // İkisi birlikte "1 Mart'a kadar rezerve edilen, Haziran kiralamaları" gibi kural kurar.
    /// <summary>Talebin yapıldığı tarih aralığının alt sınırı (erken rezervasyon kampanyası).</summary>
    public DateTimeOffset? TalepBas { get; set; }
    public DateTimeOffset? TalepBit { get; set; }

    /// <summary>Promosyon birden çok kez mi uygulanır. BİLGİ — fiyat motoru bu alanı OKUMAZ.</summary>
    public PromosyonTuru? PromosyonTuru { get; set; }
    /// <summary>Kuponun kapsamı. BİLGİ — fiyat motoru bu alanı OKUMAZ.</summary>
    public KuponGecerlilik? KuponGecerlilik { get; set; }
    /// <summary>Tutarın oran mı serbest mi olduğu. BİLGİ — fiyat motoru bu alanı OKUMAZ.</summary>
    public HesaplamaTipi? HesaplamaTipi { get; set; }
    /// <summary>Hızlı işlem kısayolunda görünsün mü (BİLGİ/ekran tercihi).</summary>
    public bool HizliIslem { get; set; }

    /// <summary>
    /// FAZ-46 — haftanın hangi günlerinde bu kural geçerli (canlı <c>kiralama_sartlari.aspx</c>).
    /// Virgülle ayrılmış 0-6 (0=Pazar … 6=Cumartesi, <see cref="DayOfWeek"/> ile AYNI numaralama).
    /// null ya da boş = kısıt yok (tüm günler) — bu fazdan önceki davranış.
    /// <b>BİLGİ:</b> fiyat motoru bu alanı henüz okumaz; kuralın tüketildiği yer ayrı bir faz
    /// (bkz. spec "Notlar" — banka-matrisi/hesaplama kararına bağlı).
    /// </summary>
    public string? HaftaGunKisiti { get; set; }
    /// <summary>FAZ-73 — kampanya arama sınıflandırması (Talep/Rezervasyon). BİLGİ ALANI: fiyat
    /// motoru geçerliliği DAİMA kira başlangıç tarihinden kontrol eder, bu alan o kontrolü
    /// DEĞİŞTİRMEZ (bkz. <see cref="KuralTarihTipi"/>).</summary>
    public KuralTarihTipi TarihTipi { get; set; } = KuralTarihTipi.Rezervasyon;

    /// <summary>Kira/rezervasyon şart metni (sözleşme/rez şartları — rezsartlar).</summary>
    public string? SartMetni { get; set; }

    /// <summary>
    /// FAZ-73 — 5 durumlu kampanya yaşam döngüsü. Fiyat motoru (ListActiveAsync) YALNIZ
    /// <see cref="KampanyaDurum.Aktif"/> kuralları okur.
    /// <para><b>Değişmez:</b> <see cref="Aktif"/> == (<c>KampanyaDurum == Aktif</c>). İki alan tek
    /// yazma noktasından (RentalRuleService.Apply) BİRLİKTE set edilir; asenkron sürüklenme yok.</para>
    /// </summary>
    public KampanyaDurum KampanyaDurum { get; set; } = KampanyaDurum.Aktif;

    /// <summary>Geriye uyum bayrağı — <see cref="KampanyaDurum"/> ile SENKRON tutulur (bkz. orada).
    /// Yeni okuma noktaları KampanyaDurum kullanmalıdır; bu kolonun kaldırılması ayrı temizlik işi.</summary>
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
