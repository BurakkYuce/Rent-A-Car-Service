namespace RentACar.Application.AracKredileri;

/// <summary>Araç kredisi oluşturma girişi (roadmap L4). FaizOran kesir (0.20 = %20 yıllık basit).</summary>
public sealed class AracKrediInput
{
    public string? BankaAdi { get; set; }
    public Guid? VehicleId { get; set; }
    /// <summary>FAZ-13: ilişkili cari. Deftere HİÇBİR kayıt yazmaz (cari bakiyesi değişmez).</summary>
    public Guid? CariId { get; set; }
    /// <summary>FAZ-13: banka/finans kurumu dosya referansı (canlıdaki "Dosya Numarası").</summary>
    public string? DosyaNo { get; set; }
    public decimal KrediTutari { get; set; }
    public decimal FaizOran { get; set; }
    public int TaksitSayisi { get; set; }
    public DateTimeOffset? BaslangicTarihi { get; set; }
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public string? Aciklama { get; set; }
}

/// <summary>Kredi taksit planı satırı.</summary>
public sealed record AracKrediTaksit(int Sira, DateTimeOffset Vade, decimal Tutar, bool Odendi);

/// <summary>
/// Kredi mali özeti + taksit planı (salt-hesap, DEFTERE YAZMAZ).
///
/// <para>FAZ-13 eklemeleri: <see cref="SonVadeGunu"/>, <see cref="SonTaksitTutari"/>,
/// <see cref="BuAyToplamTaksit"/>. <b>Faiz/taksit formülü DEĞİŞMEDİ</b> — yeni alanlar mevcut plandan
/// TÜRETİLİR; geçmiş postlanmış taksit giderleri etkilenmez.</para>
/// </summary>
/// <param name="ToplamFaiz">Basit faiz toplamı (canlıdaki "Faiz Toplam").</param>
/// <param name="ToplamGeriOdeme">Ana para + faiz (canlıdaki "Taksit Toplam").</param>
/// <param name="AylikTaksit">Eşit taksit tutarı (son taksit hariç — kalan-yöntemi).</param>
/// <param name="OdenenTutar">Ödenen taksitlerin toplamı.</param>
/// <param name="KalanBakiye">Kalan kredi borcu (canlıdaki "Toplam Kredi Borcu" tek kredi karşılığı).</param>
/// <param name="SonVadeGunu">Planın SON taksitinin vade tarihi. Canlıda "Vade Günü" ayın gününü tutar;
/// tam tarih daha bilgilendirici (gün zaten tarihin içindedir) ve ay/yıl belirsizliği bırakmaz.</param>
/// <param name="SonTaksitTutari">Son taksitin tutarı — kalan-yöntemi gereği küsürat farkını emer,
/// bu yüzden <paramref name="AylikTaksit"/>'ten farklı olabilir (canlıdaki "Taksit Tutarı").</param>
/// <param name="BuAyToplamTaksit">Referans ayda vadesi gelen taksitlerin toplamı (canlıdaki "Bu Ayki
/// Toplam Taksit"). Ödenmiş olsun olmasın o ayın YÜKÜMLÜLÜĞÜdür — nakit planlaması için okunur.</param>
public sealed record AracKrediOzet(
    decimal ToplamFaiz, decimal ToplamGeriOdeme, decimal AylikTaksit,
    decimal OdenenTutar, decimal KalanBakiye, IReadOnlyList<AracKrediTaksit> Taksitler,
    DateTimeOffset? SonVadeGunu, decimal SonTaksitTutari, decimal BuAyToplamTaksit);

/// <summary>
/// Liste üstündeki 5 özet kart (canlı `arac_kredi.aspx` özet kutularının karşılığı). Ekrandaki
/// FİLTRELİ küme üzerinden hesaplanır: cariye göre süzülünce kartlar o carinin toplamını gösterir.
///
/// <para><b>DEFTERE YAZMAZ.</b> Tümü kredi kayıtlarından türetilen göstergelerdir; hiçbir
/// <c>AccountLedgerEntry</c> üretmez ve hiçbir rapor toplamına karışmaz (KARARLAR.md genel
/// politikası). Gerçek para hareketi yalnız "Taksit Öde" akışından geçer.</para>
///
/// <para><b>İptal krediler HARİÇ</b> — iptal edilmiş kredinin borcu/faizi yoktur; toplama katmak
/// borcu şişirirdi.</para>
/// </summary>
/// <param name="ToplamFaiz">Σ kredi faizi.</param>
/// <param name="SonVadeGunu">Kümedeki EN GEÇ son-taksit vadesi (borcun bittiği gün).</param>
/// <param name="SonTaksitTutari">O en geç vadeli kredinin son taksit tutarı.</param>
/// <param name="BuAyToplamTaksit">Referans ayda vadesi gelen taksitlerin toplamı.</param>
/// <param name="ToplamKrediBorcu">Σ kalan bakiye.</param>
public sealed record AracKrediPano(
    decimal ToplamFaiz, DateTimeOffset? SonVadeGunu, decimal SonTaksitTutari,
    decimal BuAyToplamTaksit, decimal ToplamKrediBorcu);
