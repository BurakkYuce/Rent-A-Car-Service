namespace RentACar.Web.Api.Arac;

/// <summary>
/// Araç kartının TÜM düzenlenebilir alanları (Blazor <c>VehicleEdit</c> + yeni araç formu paritesi;
/// <c>VehicleInput</c> ile alan alan aynı). Enum alanları ADLA taşınır (<c>Durum</c>: <c>Musait</c>…;
/// <c>FiloDurum</c>, <c>Vites</c>, <c>Yakit</c> boş = belirtilmedi). PUT tam değiştirmedir: gönderilmeyen alan boşalır.
/// </summary>
public record AracIstegi
{
    public string? Plaka { get; init; }
    public string? Marka { get; init; }
    public string? Tip { get; init; }
    /// <summary>Grup adı. Boş + <see cref="GrupBilincliBos"/> false → oluştururken firmanın varsayılan grubu.</summary>
    public string? Grup { get; init; }
    /// <summary>"(Grupsuz)" BİLİNÇLİ seçildi — varsayılana düşürülmez (Blazor PR-10 kuralı).</summary>
    public bool GrupBilincliBos { get; init; }
    public int? VitrinAdet { get; init; }
    public string? Segment { get; init; }
    public string? Sipp { get; init; }
    public string? Renk { get; init; }
    public int? ModelYili { get; init; }
    public string? Vites { get; init; }
    public string? SasiNo { get; init; }
    public string? MotorNo { get; init; }
    public string? Sube { get; init; }
    public string? Durum { get; init; }
    public string? FiloDurum { get; init; }
    public int Km { get; init; }
    public string? Yakit { get; init; }

    public int? MotorGucu { get; init; }
    public int? SilindirHacmi { get; init; }
    public string? RuhsatNo { get; init; }
    public DateTimeOffset? TescilTarihi { get; init; }
    public string? AracSahibi { get; init; }
    public decimal? AlimBedeli { get; init; }
    public DateTimeOffset? AlimTarihi { get; init; }
    public decimal? AlisVergisiz { get; init; }
    public decimal? AlisOtv { get; init; }
    public decimal? AlisKdv { get; init; }
    public decimal? AylikMaliyet { get; init; }
    public decimal? FiloYonetimMaliyeti { get; init; }
    public decimal? IkinciElDeger { get; init; }
    public DateTimeOffset? FiloGirisTarih { get; init; }
    public DateTimeOffset? FiloCikisTarih { get; init; }
    public string? OzelKod1 { get; init; }
    public string? OzelKod2 { get; init; }
    public string? OzelKod3 { get; init; }
    public string? OzelKod4 { get; init; }
    public string? OzelKod5 { get; init; }

    public string? BelgeNo { get; init; }
    public string? RuhsatSahibi { get; init; }
    public string? SozNo { get; init; }
    public string? AraciAlan { get; init; }
    public string? Kiralayan { get; init; }
    public string? AssistanFirma { get; init; }
    public string? TsbKodu { get; init; }
    public string? OdemeSekli { get; init; }
    public string? PasifSebep { get; init; }
    public string? SonDurum { get; init; }
    public string? HgsFirma { get; init; }
    public int? SonTeslimKm { get; init; }
    public int? KiraGun { get; init; }
    public int? DisKmLimit { get; init; }
    public decimal? KiraFiyat { get; init; }
    public decimal? TsbKaskoDegeri { get; init; }
    public decimal? AlisEuroFiyat { get; init; }
    public decimal? SatisEuroFiyat { get; init; }
    public DateTimeOffset? SonTeslimTarihi { get; init; }
    public DateTimeOffset? KiraBitTar { get; init; }
    public DateTimeOffset? KiraBekTar { get; init; }
    /// <summary>Not alanı (kiralayan cari). Doluysa bu kiracıda VAR olmalı.</summary>
    public Guid? KiraMusteriId { get; init; }
    public bool? AlisEuro { get; init; }

    public string? HgsNo { get; init; }
    public string? OgsNo { get; init; }
    public string? KasaTipi { get; init; }
    public string? DetayTipi { get; init; }
    public string? AlimFaturaNo { get; init; }
    public string? AlimYapilanFirma { get; init; }
    public int? KiraKmLimiti { get; init; }

    public bool WebRezKapat { get; init; }
    public bool OfisRezKapat { get; init; }
    public bool ZIzni { get; init; }
    public bool Utts { get; init; }
    public bool KarLastigi { get; init; }
    public bool YedekAnahtar { get; init; }
    public bool Temizlik { get; init; }
    public bool Rehin { get; init; }
    public DateTimeOffset? SonBakimTarih { get; init; }
    public int? SonBakimKm { get; init; }
    public string? LastikDurumu { get; init; }

    public string? TsrbMarkaKodu { get; init; }
    public string? TsrbTipKodu { get; init; }
    public string? AltGrupAdi { get; init; }
    public string? EntegrasyonKodu { get; init; }
    public string? TeypKodu { get; init; }
    public string? TakipMarka { get; init; }
    public string? TakipNo { get; init; }
    public string? SahipGrup { get; init; }
    public string? AracSahibiNo { get; init; }
    public string? AracSahibi2 { get; init; }
    public string? KrediFirma { get; init; }
    public DateTimeOffset? KapatmaTarih { get; init; }
    public DateTimeOffset? CikmasiPlananTarih { get; init; }
    public int? AracSatisKm { get; init; }
    public string? Aciklama { get; init; }
    public string? Konum { get; init; }
    public decimal? AlimBedeliKur { get; init; }
    public decimal? Arac2FiyatKur { get; init; }
    public decimal? SimdiKur { get; init; }
    public decimal? AylikMaliyetDoviz { get; init; }
}

/// <summary><c>PUT /araclar/{id}</c> — <c>surum</c> ZORUNLU (uyuşmazlık 409 <c>cakisma</c>).</summary>
public sealed record AracGuncelleIstegi : AracIstegi
{
    public string? Surum { get; init; }
}

/// <summary>Araç kartı (düzenleme formu). <c>Surum</c> alanlardan ÖNCE okunur.</summary>
public sealed record AracKartDto : AracIstegi
{
    public Guid Id { get; init; }
    public string? Surum { get; init; }
    public Guid? SubeId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record AracKmIstegi(int? Km, DateTimeOffset? Tarih);
