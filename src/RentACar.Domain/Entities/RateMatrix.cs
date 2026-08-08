using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tarife matrisi (canlı TürevRent "XML Tarife" / tarifeler_xml karşılığı): asıl günlük kira fiyat
/// listesi. Kanal (Rez Kaynağı) + şube + lokasyon + araç grubu bazında, geçerlilik tarihli,
/// onay iş akışlı, gün-kademesi (1..7) başına günlük fiyat matrisi + dinamik indirim oranı.
/// Tenant-owned + auditable. Saf fiyat-tanım tablosu — deftere kayıt POSTLAMAZ; fiyat motorunun
/// (parite #7) birincil girdisidir. <see cref="Kod"/> tenant içinde benzersiz (matris satırı kimliği).
/// </summary>
public class RateMatrix : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Matris satırı kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    /// <summary>Tarife adı.</summary>
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Kapsam (hangi kanal/şube/lokasyon/grup için)
    /// <summary>Kanal = Rez Kaynağı (ör. WEB, ACENTA, ÇAĞRI).</summary>
    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK (roadmap F1; metin korunur)

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }
    public string? Lokasyon { get; set; }
    /// <summary>Araç grubu kodu (RateMatrix grup bazlıdır; VehicleGroup.Kod'a serbest metin referans).</summary>
    public string? AracGrupKod { get; set; }
    /// <summary>Para birimi (ör. TRY, EUR). Boş → tenant varsayılanı.</summary>
    public string? ParaBirimi { get; set; }

    // Geçerlilik
    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    // Gün-kademesi başına günlük fiyat (Gün 1..7)
    public decimal? Gun1 { get; set; }
    public decimal? Gun2 { get; set; }
    public decimal? Gun3 { get; set; }
    public decimal? Gun4 { get; set; }
    public decimal? Gun5 { get; set; }
    public decimal? Gun6 { get; set; }
    public decimal? Gun7 { get; set; }

    /// <summary>Uzun-dönem kademeleri (FAZ 3.A1) — additive: 8-29 gün için HAFTALIK-kademe günlüğü,
    /// 30+ gün için AYLIK-kademe günlüğü. Null = kademe tanımsız → bugünkü Gun7-clamp davranışı
    /// (geriye uyum; AddColumn = sıfır backfill). Bracket tablosu DEĞİL — onay akışı/RLS/UI satır-bazlı.</summary>
    public decimal? GunHaftalik { get; set; }
    public decimal? GunAylik { get; set; }

    // ---- FAZ-71 — KADEME BAZLI KM LİMİTİ + AŞIM ÜCRETİ ----
    // Canlı TürevRent'te her gün-kademesinin KENDİ km limiti ve KENDİ aşım ücreti var; bizde bu
    // değer araç grubunda TEK/GLOBAL'di (VehicleGroup.GunlukKmLimiti/AsimKmUcreti).
    //
    // KULLANICI KARARI 1 — Km değeri GÜNLÜK limittir, kira gününe ÇARPILIR:
    //   "günlük 200 girdin ve 5 günlük kiralama → müşteri 1000 km katedene kadar ek km çıkmaz."
    //   Mevcut VehicleGroup.GunlukKmLimiti ile AYNI dil; iki yer farklı kural konuşmaz.
    //
    // KULLANICI KARARI 2 — uzun dönem için AYRI alanlar: canlı yalnız 6 kademe tanımlıyor ama bizim
    // fiyat kademelerimiz 7 gün + haftalık(8-29) + aylık(30+) diye devam ediyor. Bu kademeleri
    // Km6'ya düşürmek yerine KENDİ km alanları verildi — uzun kirada limit "son kademeden miras"
    // kalmaz, operatör açıkça belirler.
    //
    // KAPSAM SINIRI: bunlar YALNIZ ön-izleme tahminini besler. KM-aşım PARASININ tek otoritesi
    // dönüş-zamanı hesabıdır (ReturnMath, KURAL A); bu alanlar RentalContract.KmLimit/FazlaKmUcret'i
    // DOLDURMAZ ve deftere hiçbir şey yazmaz.

    public int? Km1 { get; set; }
    public int? Km2 { get; set; }
    public int? Km3 { get; set; }
    public int? Km4 { get; set; }
    public int? Km5 { get; set; }
    public int? Km6 { get; set; }
    public decimal? Km1Ucret { get; set; }
    public decimal? Km2Ucret { get; set; }
    public decimal? Km3Ucret { get; set; }
    public decimal? Km4Ucret { get; set; }
    public decimal? Km5Ucret { get; set; }
    public decimal? Km6Ucret { get; set; }

    /// <summary>Haftalık kademe (8-29 gün) günlük km limiti. Null → Km6'ya, o da boşsa gruba düşer.</summary>
    public int? KmHaftalik { get; set; }
    public decimal? KmHaftalikUcret { get; set; }
    /// <summary>Aylık kademe (30+ gün) günlük km limiti. Null → KmHaftalik → Km6 → grup.</summary>
    public int? KmAylik { get; set; }
    public decimal? KmAylikUcret { get; set; }

    /// <summary>Karşılaştırma sistemi (rakip) dinamik indirim oranı % (canlı Max_Esneklik).
    /// A6 KARARI: fiyat GİRDİSİ DEĞİLDİR (motor okumaz) — salt-görünüm rakip-kıyas notu; kolon parite için kalır.</summary>
    public decimal? MaxEsneklik { get; set; }

    // Onay iş akışı
    public TarifeOnayDurumu OnayDurumu { get; set; } = TarifeOnayDurumu.Bekliyor;
    public string? Onaylayan { get; set; }
    public DateTimeOffset? OnayZaman { get; set; }

    public bool Aktif { get; set; } = true;


    // ---- FAZ-70 ----
    /// <summary>Satır etiketi: "Fiyat" / "Kampanya". SALT ETİKET — motor davranışını DEĞİŞTİRMEZ,
    /// yalnız listede ayırt etmeye yarar.</summary>
    public string? Turu { get; set; }

    /// <summary>
    /// Max Kira Kapsamı (gün). Bu satır YALNIZ süresi bu değeri aşmayan kiralarda geçerlidir;
    /// aşıldığında satır ADAYLIKTAN ELENİR ve motor sıradaki uygun satıra düşer (yeni bir RED yolu
    /// açılmaz — eşleşen satır kalmazsa mevcut "tarife bulunamadı" akışı işler).
    /// null = sınırsız.
    /// </summary>
    public int? KiraSuresi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
