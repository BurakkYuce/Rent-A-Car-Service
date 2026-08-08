using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>Araç oluştur/düzenle için giriş modeli (Blazor formu da buna bağlanır).</summary>
public sealed class VehicleInput
{
    public string Plaka { get; set; } = string.Empty;
    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }
    /// <summary>PR-10: <c>true</c> ise <see cref="Grup"/> BİLİNÇLİ olarak boş bırakılmıştır (web
    /// formundaki "(Grupsuz)" seçeneği) → varsayılan grup UYGULANMAZ. <c>false</c> (varsayılan): alan
    /// hiç belirtilmemiştir (REST API / Excel import) → boşsa varsayılan grup uygulanır.
    /// Ayrım şart: ikisi de boş string olarak gelseydi kullanıcının "grupsuz kalsın" kararı sessizce
    /// geri alınırdı. Web ucu bunu <c>form.ContainsKey("grup")</c> ile üretir (select daima post eder).</summary>
    public bool GrupBilincliBos { get; set; }
    /// <summary>PR-11: halka açık sitede kaç araç olarak gösterilsin (null = 1; 1..999). YALNIZ
    /// görüntüleme — müsaitlik/rezervasyon sonucunu etkilemez (bkz. <see cref="Domain.Entities.Vehicle.VitrinAdet"/>).</summary>
    public int? VitrinAdet { get; set; }
    public string? Segment { get; set; }
    public string? Sipp { get; set; }
    public string? Renk { get; set; }
    public int? ModelYili { get; set; }
    public Vites? Vites { get; set; }
    public string? SasiNo { get; set; }
    public string? MotorNo { get; set; }
    public string? Sube { get; set; }
    public VehicleStatus Durum { get; set; } = VehicleStatus.Musait;
    public FiloStatus? FiloDurum { get; set; }
    public int Km { get; set; }
    public FuelType? Yakit { get; set; }   // PR-21: null = "belirtilmedi"

    // Parite zenginleştirme (additive, opsiyonel)
    public int? MotorGucu { get; set; }
    public int? SilindirHacmi { get; set; }
    public string? RuhsatNo { get; set; }
    public DateTimeOffset? TescilTarihi { get; set; }
    public string? AracSahibi { get; set; }
    public decimal? AlimBedeli { get; set; }
    public DateTimeOffset? AlimTarihi { get; set; }
    public decimal? AlisVergisiz { get; set; }
    public decimal? AlisOtv { get; set; }
    public decimal? AlisKdv { get; set; }
    public decimal? AylikMaliyet { get; set; }
    public decimal? FiloYonetimMaliyeti { get; set; }
    public decimal? IkinciElDeger { get; set; }
    public DateTimeOffset? FiloGirisTarih { get; set; }
    public DateTimeOffset? FiloCikisTarih { get; set; }
    public string? OzelKod1 { get; set; }
    public string? OzelKod2 { get; set; }
    public string? OzelKod3 { get; set; }
    public string? OzelKod4 { get; set; }
    public string? OzelKod5 { get; set; }

    // ---- FAZ-28 detay alanları (bilgi; fiyat/defter/müsaitlik hesabına girmez) ----
    public string? BelgeNo { get; set; }
    public string? RuhsatSahibi { get; set; }
    public string? SozNo { get; set; }
    public string? AraciAlan { get; set; }
    public string? Kiralayan { get; set; }
    public string? AssistanFirma { get; set; }
    public string? TsbKodu { get; set; }
    public string? OdemeSekli { get; set; }
    public string? PasifSebep { get; set; }
    public string? SonDurum { get; set; }
    public string? HgsFirma { get; set; }
    public int? SonTeslimKm { get; set; }
    public int? KiraGun { get; set; }
    public int? DisKmLimit { get; set; }
    public decimal? KiraFiyat { get; set; }
    public decimal? TsbKaskoDegeri { get; set; }
    public decimal? AlisEuroFiyat { get; set; }
    public decimal? SatisEuroFiyat { get; set; }
    public DateTimeOffset? SonTeslimTarihi { get; set; }
    public DateTimeOffset? KiraBitTar { get; set; }
    public DateTimeOffset? KiraBekTar { get; set; }
    public Guid? KiraMusteriId { get; set; }
    public bool? AlisEuro { get; set; }

    // roadmap G1
    public string? HgsNo { get; set; }
    public string? OgsNo { get; set; }
    public string? KasaTipi { get; set; }
    public string? DetayTipi { get; set; }
    public string? AlimFaturaNo { get; set; }
    public string? AlimYapilanFirma { get; set; }
    public int? KiraKmLimiti { get; set; }

    // Operasyon bayrakları (roadmap K2)
    public bool WebRezKapat { get; set; }
    public bool OfisRezKapat { get; set; }
    public bool ZIzni { get; set; }
    public bool Utts { get; set; }
    public bool KarLastigi { get; set; }
    public bool YedekAnahtar { get; set; }
    public bool Temizlik { get; set; }
    public bool Rehin { get; set; }
    // Bakım/lastik (roadmap K2)
    public DateTimeOffset? SonBakimTarih { get; set; }
    public int? SonBakimKm { get; set; }
    public string? LastikDurumu { get; set; }

    // ---- FAZ-10 araç kartı derinliği (bilgi; fiyat/defter/müsaitlik/karne hesabına GİRMEZ) ----
    public string? TsrbMarkaKodu { get; set; }
    public string? TsrbTipKodu { get; set; }
    public string? AltGrupAdi { get; set; }
    public string? EntegrasyonKodu { get; set; }
    public string? TeypKodu { get; set; }
    public string? TakipMarka { get; set; }
    public string? TakipNo { get; set; }
    public string? SahipGrup { get; set; }
    public string? AracSahibiNo { get; set; }
    public string? AracSahibi2 { get; set; }
    public string? KrediFirma { get; set; }
    public DateTimeOffset? KapatmaTarih { get; set; }
    public DateTimeOffset? CikmasiPlananTarih { get; set; }
    public int? AracSatisKm { get; set; }
    public string? Aciklama { get; set; }
    public string? Konum { get; set; }
    // Kur alanları SALT BİLGİ — hiçbir P&L/karne sorgusu okumaz (bkz. Vehicle.cs kararı).
    public decimal? AlimBedeliKur { get; set; }
    public decimal? Arac2FiyatKur { get; set; }
    public decimal? SimdiKur { get; set; }
    public decimal? AylikMaliyetDoviz { get; set; }
}
