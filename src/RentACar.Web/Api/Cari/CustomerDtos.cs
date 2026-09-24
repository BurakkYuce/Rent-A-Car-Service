namespace RentACar.Web.Api.Cari;

/// <summary>
/// Cari kartının GİZLİ OLMAYAN düzenlenebilir alanları (Blazor <c>CustomerEdit</c> + yeni cari formu paritesi;
/// <c>CustomerInput</c> ile alan alan aynı). İstek ve kart bunu paylaşır; TC/ehliyet/pasaport/şifre yalnız istekte
/// (<see cref="CustomerRequest"/>) vardır — kart onları hiçbir koşulda düz metin olarak taşımaz (KVKK).
/// </summary>
public record CustomerFields
{
    /// <summary><c>Bireysel</c> | <c>Kurumsal</c> | <c>Servis</c> (boş → Bireysel).</summary>
    public string? Tip { get; init; }
    public string? Ad { get; init; }
    public string? Soyad { get; init; }
    public string? Unvan { get; init; }
    public string? VergiDairesi { get; init; }
    public string? VergiNo { get; init; }
    public string? CepTel { get; init; }
    public string? Gsm2 { get; init; }
    public string? Email { get; init; }
    public string? Il { get; init; }
    public string? Ilce { get; init; }
    public string? Adres { get; init; }
    public string? Kaynak { get; init; }
    public string? MusteriTemsilcisi { get; init; }
    public bool IysIzinli { get; init; }
    public bool Uyari { get; init; }
    public string? UyariNedeni { get; init; }
    public string? EhliyetSinifi { get; init; }
    public DateTimeOffset? EhliyetTarihi { get; init; }
    public string? EhliyetYeri { get; init; }
    public string? Tarife { get; init; }
    public int VadeGun { get; init; }
    public decimal RiskLimiti { get; init; }
    public string? RiskMesaji { get; init; }
    public DateTimeOffset? RiskTarihi { get; init; }
    public string? HgsYansitmaTuru { get; init; }
    public bool KaraListe { get; init; }
    public bool Pasif { get; init; }
    public string? OzelCariTip { get; init; }
    public string? MusteriTipi { get; init; }
    public string? EhliyetUlke { get; init; }
    public string? Dil { get; init; }
    public string? Doviz { get; init; }
    public string? TevkifatDurum { get; init; }
    public string? Sinif { get; init; }
    public bool? MailIzin { get; init; }
    public bool? SmsIzin { get; init; }
    public bool? TelefonIzin { get; init; }
    public DateTimeOffset? DogumTarihi { get; init; }
    public string? BabaAdi { get; init; }
    public string? AnaAdi { get; init; }
    public string? FaturaDonemi { get; init; }
    public decimal? TevkifatOrani { get; init; }
    /// <summary>Kurumsal yetkili kişiler (en çok 30; ad-soyadı boş satır atlanır).</summary>
    public IReadOnlyList<CustomerContactDto>? Kisiler { get; init; }
    public bool? KvkkOnay { get; init; }
    public DateTimeOffset? KvkkOnayTarih { get; init; }
    public string? EkAdres { get; init; }
    public string? BankaIban { get; init; }
    public string? BankaAdi { get; init; }
    public string? FaturaAdresi { get; init; }
    public string? FaturaUnvan { get; init; }
    public string? Ulke { get; init; }
    public string? Tel2 { get; init; }
    public string? OzelKod { get; init; }
    public string? EntegrasyonKodu { get; init; }
    public string? Aciklama { get; init; }
    public string? RiskIzin { get; init; }
    public string? DogumYeri { get; init; }
    public string? PasaportYeri { get; init; }
    public DateTimeOffset? PasaportTarihi { get; init; }
    public string? KurumsalNo { get; init; }
    public string? UyariSerbest { get; init; }
    public string? TevkifatKodu { get; init; }
    public string? FaturaKiralayanIsim { get; init; }
    public string? IsAdresi { get; init; }
    public string? IsTelefonu { get; init; }
    public string? KayitliIl { get; init; }
    public string? KayitliIlce { get; init; }
    public string? MahalleKoy { get; init; }
    public string? SeriNo { get; init; }
    public string? CiltNo { get; init; }
    public string? AileSira { get; init; }
    public string? SiraNo { get; init; }
    public bool TcDogrulama { get; init; }
    public bool FaturaAdresFarkli { get; init; }
    public bool FaturaTekSatir { get; init; }
    public bool DogumGunuTakip { get; init; }
    public bool AnonimAd { get; init; }
    public bool AnonimTc { get; init; }
    public bool AnonimTelefon { get; init; }
    public bool AnonimMail { get; init; }
    public bool AnonimAdres { get; init; }
    public bool AnonimBelge { get; init; }
    public bool BakiyeGor { get; init; }
    public bool AracVerilmez { get; init; }
    public bool YasEhliyetSerbest { get; init; }
    public bool MerkezKurumsal { get; init; }
    public bool Broker { get; init; }
    public bool FindexZorunlu { get; init; }
    public decimal? BayiKomisyon { get; init; }
    public decimal? WebIndirim { get; init; }
    public DateTimeOffset? KaraZamani { get; init; }
    public Guid? IslemSubeId { get; init; }
    public Guid? FirmaId { get; init; }
}

public sealed record CustomerContactDto(string? AdSoyad, string? Telefon, string? Mail, string? Gorev);

/// <summary>
/// Cari oluştur/güncelle isteği. Gizli alanlar YALNIZ YAZILIR (yanıtta dönmez):
/// <list type="bullet">
/// <item><c>tcKimlik</c>, <c>ehliyetNo</c>, <c>pasaportNo</c>: güncellemede <c>null</c> = DEĞİŞTİRME (kart bunları
/// göstermediği için tam değiştirme PUT'u onları sessizce silmesin); <c>""</c> = temizle; dolu = yeni değer.</item>
/// <item><c>sifre</c>: boş = değiştirme (portal şifresi; tek yönlü özet olarak saklanır).</item>
/// <item>KVKK ile gizlenen alan grupları (<c>Anonim*</c> bayrağı kayıtlıysa kartta <c>null</c> döner): o grubun alanında
/// <c>null</c> = DEĞİŞTİRME.</item>
/// </list>
/// </summary>
public record CustomerRequest : CustomerFields
{
    public string? TcKimlik { get; init; }
    public string? EhliyetNo { get; init; }
    public string? PasaportNo { get; init; }
    public string? Sifre { get; init; }
}

/// <summary>Tam değiştirme: <see cref="Surum"/> ZORUNLU (kartın <c>surum</c>'u); uyuşmazlık 409 <c>cakisma</c>.</summary>
public sealed record CustomerUpdateRequest : CustomerRequest
{
    public string? Surum { get; init; }
}

/// <summary>
/// Cari kartı (düzenleme formu). TC hiçbir koşulda dönmez (yalnız <see cref="TcKimlikVar"/>); ehliyet/pasaport numarası
/// maskeli; <c>Anonim*</c> bayrağı işaretli grubun alanları <c>null</c> (<c>MusteriGorunumu</c> grupları).
/// </summary>
public sealed record CustomerCardDto : CustomerFields
{
    public Guid Id { get; init; }
    /// <summary>Tam değiştirme PUT'unda geri gönderilecek sürüm.</summary>
    public string? Surum { get; init; }
    public bool TcKimlikVar { get; init; }
    public string? EhliyetNoMaske { get; init; }
    public string? PasaportNoMaske { get; init; }
    /// <summary>Bireysel caride vergi no yalnız maskeli döner (<c>vergiNo</c> null; PUT'ta null = koru, "" = temizle).</summary>
    public string? VergiNoMaske { get; init; }
    /// <summary>Portal şifresi tanımlı mı (özet asla dönmez).</summary>
    public bool SifreVar { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

/// <summary>Cari liste satırı. TC YOK; bireysel caride vergi no da dönmez (şahıs vergi no = TC olabilir).</summary>
public sealed record CustomerListRow(
    Guid Id, string Tip, string Ad, bool Anonim, string? VergiNo, string? CepTel, string? Gsm2, string? Email,
    string? Il, string? Ilce, string? Kaynak, string? MusteriTemsilcisi, string? EntegrasyonKodu, string? OzelKod,
    string? Sinif, string? Ulke, int? VadeGun, int KiraAdet, decimal Ciro, DateTimeOffset? SonKira,
    bool KaraListe, bool Pasif, bool Uyari, string? UyariNedeni, bool IysIzinli, bool AracVerilmez);

public sealed record CustomerRentalSummary(
    Guid Id, string SozlesmeNo, DateTimeOffset BasTar, DateTimeOffset BitTar, string Durum, decimal GenelToplam,
    decimal Bakiye, string? Doviz);

public sealed record CustomerLedgerLine(
    DateTimeOffset Tarih, string Kaynak, string? Aciklama, decimal? Borc, decimal? Alacak);

/// <summary>
/// Cari detayı (360°). Müşteri özeti <c>MusteriGorunumu</c> kuralıyla. <see cref="Kiralar"/> şube kapsamına süzülür
/// (şubeye bağlı kullanıcı başka şubenin kirasını görmez). <see cref="Bakiye"/>/<see cref="Hareketler"/> yalnız
/// FinanceWrite ya da ViewReports izniyle dolu, aksi halde <c>null</c> (cari defteri firma geneli finans verisidir).
/// </summary>
public sealed record CustomerDetailView(
    Kira.KiraMusteriOzeti Musteri, bool KaraListe, bool Pasif, bool AracVerilmez,
    IReadOnlyList<CustomerRentalSummary> Kiralar, decimal? Bakiye, IReadOnlyList<CustomerLedgerLine>? Hareketler);
