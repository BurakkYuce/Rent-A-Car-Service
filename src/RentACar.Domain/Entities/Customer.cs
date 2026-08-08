using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Cari (müşteri). Bireysel / Kurumsal / Servis. PR #2 çekirdeği — orijinal
/// Musteri_Kayit.Aspx'in tüm alanları değil. Tenant-owned + auditable: EF filter +
/// RLS ile izole, değişiklikleri AuditLog'a yazılır.
/// Benzersizlik (tenant içinde): bireysel için TC Kimlik, kurumsal için Vergi No
/// (her ikisi de yalnız doluyken — kısmi unique index).
/// </summary>
public class Customer : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public CariType Tip { get; set; } = CariType.Bireysel;

    // Bireysel
    public string? Ad { get; set; }
    public string? Soyad { get; set; }

    /// <summary>ESKİ düz-metin TC kolonu — KVKK/F2 sonrası uygulama YAZMAZ; başlangıç
    /// backfill'i şifreleyip null'lar. Bir sonraki sürümde kolon düşürülecek.</summary>
    public string? TcKimlik { get; set; }

    // ---- KVKK/F2: PII at-rest şifreli + blind-index (Personel D1 deseni + hash) ----
    /// <summary>TC Kimlik — ISecretProtector cipher'ı (görüntülemede çözülür).</summary>
    public string? TcKimlikEnc { get; set; }
    /// <summary>TC Kimlik HMAC blind-index — tenant-içi benzersizlik + tam-eşleşme arama.</summary>
    public string? TcKimlikHash { get; set; }

    // Kurumsal / Servis
    public string? Unvan { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }

    // İletişim / adres
    public string? CepTel { get; set; }
    public string? Gsm2 { get; set; }
    public string? Email { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Adres { get; set; }

    // CRM
    /// <summary>Müşteri kaynağı (ör. Web, Telefon, Bayi, Tavsiye).</summary>
    public string? Kaynak { get; set; }
    public string? MusteriTemsilcisi { get; set; }
    /// <summary>İYS (ileti yönetim sistemi) izinli mi?</summary>
    public bool IysIzinli { get; set; }
    /// <summary>Operasyonel uyarı bayrağı (kara listeden ayrı, hafif uyarı).</summary>
    public bool Uyari { get; set; }
    public string? UyariNedeni { get; set; }

    // ---- CRM parite zenginleştirme (docs/parite/03; additive, NULLABLE) ----
    /// <summary>Müşteri sınıfı/segment (canlı Mst_Sinif: Düşük/Orta/Yüksek/VIP/Personel/Problemli).</summary>
    public string? Sinif { get; set; }
    /// <summary>Kanal-bazlı izin: e-posta (İYS'den ayrı, kanal granülerliği).</summary>
    public bool? MailIzin { get; set; }
    /// <summary>Kanal-bazlı izin: SMS.</summary>
    public bool? SmsIzin { get; set; }
    /// <summary>Kanal-bazlı izin: telefon arama.</summary>
    public bool? TelefonIzin { get; set; }
    public DateTimeOffset? DogumTarihi { get; set; }
    public string? BabaAdi { get; set; }
    public string? AnaAdi { get; set; }
    /// <summary>ESKİ düz-metin pasaport kolonu — uygulama yazmaz; backfill şifreleyip null'lar.</summary>
    public string? PasaportNo { get; set; }
    /// <summary>Pasaport No — şifreli (KVKK/F2).</summary>
    public string? PasaportNoEnc { get; set; }
    /// <summary>Fatura dönemi (ör. Aylık, 15 günlük, Peşin).</summary>
    public string? FaturaDonemi { get; set; }
    /// <summary>Tevkifat oranı (% — kurumsal stopaj).</summary>
    public decimal? TevkifatOrani { get; set; }

    // Kurumsal yetkili kişiler (×3)
    public string? Yetkili1Ad { get; set; }
    public string? Yetkili1Tel { get; set; }
    public string? Yetkili1Mail { get; set; }
    public string? Yetkili2Ad { get; set; }
    public string? Yetkili2Tel { get; set; }
    public string? Yetkili2Mail { get; set; }
    public string? Yetkili3Ad { get; set; }
    public string? Yetkili3Tel { get; set; }
    public string? Yetkili3Mail { get; set; }

    /// <summary>Kurumsal cari yetkili kişileri (PR-E): DEĞİŞKEN sayıda child (Yetkili1-3 flat kolonlarının
    /// yerine; flat'ler DEPRECATED — uygulama yazmaz, göç bir kez taşıdı). İletişim bilgisi (PII değil).</summary>
    public List<CustomerContact> Kisiler { get; set; } = [];

    // Ehliyet
    /// <summary>ESKİ düz-metin ehliyet kolonu — uygulama yazmaz; backfill şifreleyip null'lar.</summary>
    public string? EhliyetNo { get; set; }
    /// <summary>Ehliyet No — şifreli (KVKK/F2).</summary>
    public string? EhliyetNoEnc { get; set; }
    public string? EhliyetSinifi { get; set; }
    public DateTimeOffset? EhliyetTarihi { get; set; }
    public string? EhliyetYeri { get; set; }

    // Finans / risk
    public string? Tarife { get; set; }
    public int VadeGun { get; set; }
    public decimal RiskLimiti { get; set; }
    public string? RiskMesaji { get; set; }
    public DateTimeOffset? RiskTarihi { get; set; }
    /// <summary>HGS/geçiş yansıtma türü (ör. Faturalı, Faturasız, Yansıtılmaz).</summary>
    public string? HgsYansitmaTuru { get; set; }

    // ---- TürevRent parite (additive, nullable) ----
    public string? OzelCariTip { get; set; }   // Yurtiçi/Yurtdışı/2.El/Grup İçi/Standart
    public string? MusteriTipi { get; set; }   // Türk Ehliyetli/Yabancı Ehliyetli/Türk-Yabancı Ehliyetli
    public string? EhliyetUlke { get; set; }   // ehliyeti veren ülke
    public string? Dil { get; set; }           // TR/EN/Diğer
    public string? Doviz { get; set; }         // cari varsayılan döviz (TL/EURO/USD)
    public string? TevkifatDurum { get; set; } // Serbest/Sadece Tevkifatsız/Sadece Tevkifatlı

    public bool KaraListe { get; set; }
    public bool Pasif { get; set; }

    // ---- KVKK + ek adres/banka/fatura adresi (roadmap K4; additive, nullable) ----
    /// <summary>KVKK açık rıza onayı verildi mi.</summary>
    public bool? KvkkOnay { get; set; }
    /// <summary>KVKK onay tarihi.</summary>
    public DateTimeOffset? KvkkOnayTarih { get; set; }
    /// <summary>İkincil/ek adres.</summary>
    public string? EkAdres { get; set; }
    /// <summary>Banka IBAN (iade/ödeme için).</summary>
    public string? BankaIban { get; set; }
    /// <summary>Banka adı.</summary>
    public string? BankaAdi { get; set; }
    /// <summary>Faturada kullanılacak farklı adres.</summary>
    public string? FaturaAdresi { get; set; }
    /// <summary>Faturada kullanılacak farklı ünvan.</summary>
    public string? FaturaUnvan { get; set; }

    // ---- FAZ-40 derinlik (additive, hepsi nullable/false — mevcut kayıtlar etkilenmez) ----

    public bool TcDogrulama { get; set; }
    public string? Ulke { get; set; }
    public string? Tel2 { get; set; }
    public string? OzelKod { get; set; }
    public string? EntegrasyonKodu { get; set; }
    public string? Aciklama { get; set; }
    public bool FaturaAdresFarkli { get; set; }
    public string? RiskIzin { get; set; }
    public decimal? BayiKomisyon { get; set; }
    /// <summary>Fatura tek satırda kesilsin (kalem dökümü olmadan).</summary>
    public bool FaturaTekSatir { get; set; }
    public bool DogumGunuTakip { get; set; }

    // KVKK anonimleştirme bayrakları — kayıt SİLİNMEZ, alan bazında maskeleme talebi işaretlenir.
    public bool AnonimAd { get; set; }
    public bool AnonimTc { get; set; }
    public bool AnonimTelefon { get; set; }
    public bool AnonimMail { get; set; }
    public bool AnonimAdres { get; set; }
    public bool AnonimBelge { get; set; }

    public string? DogumYeri { get; set; }
    public DateTimeOffset? PasaportTarihi { get; set; }
    public string? PasaportYeri { get; set; }
    public string? KurumsalNo { get; set; }

    /// <summary>
    /// Portal şifresinin TEK YÖNLÜ ÖZETİ. Düz metin ASLA saklanmaz — servis
    /// <c>IPasswordHasher</c> ile hash'ler (TarifeGrubu ile aynı desen). Boş şifre = "değiştirme".
    /// </summary>
    public string? SifreHash { get; set; }

    /// <summary>Serbest uyarı metni (mevcut <c>Uyari</c> bayrağından ayrı; o bayrak, bu açıklama).</summary>
    public string? UyariSerbest { get; set; }
    public decimal? WebIndirim { get; set; }
    /// <summary>Kara listeye alınma zamanı (bayrak <c>KaraListe</c>'de).</summary>
    public DateTimeOffset? KaraZamani { get; set; }
    /// <summary>İşlem şubesi referansı — FK KISITI YOK (additive; şube silinse kayıt kalır).</summary>
    public Guid? IslemSubeId { get; set; }
    public bool BakiyeGor { get; set; }
    public string? TevkifatKodu { get; set; }
    /// <summary>Bu cariye ARAÇ VERİLMEZ (operasyonel uyarı — kira açılışında gösterilir).</summary>
    public bool AracVerilmez { get; set; }
    /// <summary>Yaş/ehliyet kuralından muaf.</summary>
    public bool YasEhliyetSerbest { get; set; }
    /// <summary>Faturada kiralayan ismi farklı yazılsın.</summary>
    public string? FaturaKiralayanIsim { get; set; }
    public bool MerkezKurumsal { get; set; }
    public bool Broker { get; set; }
    public bool FindexZorunlu { get; set; }
    public string? IsAdresi { get; set; }
    public string? IsTelefonu { get; set; }
    /// <summary>Bağlı olduğu firma carisi (kurumsal hiyerarşi) — FK kısıtı YOK.</summary>
    public Guid? FirmaId { get; set; }
    public string? KayitliIl { get; set; }
    public string? KayitliIlce { get; set; }
    public string? MahalleKoy { get; set; }
    public string? SeriNo { get; set; }
    public string? CiltNo { get; set; }
    public string? AileSira { get; set; }
    public string? SiraNo { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Görünen ad: kurumsal → Ünvan; bireysel → "Ad Soyad". (Mapped değil.)</summary>
    public string DisplayName => Tip == CariType.Bireysel
        ? $"{Ad} {Soyad}".Trim()
        : (Unvan ?? string.Empty);
}
