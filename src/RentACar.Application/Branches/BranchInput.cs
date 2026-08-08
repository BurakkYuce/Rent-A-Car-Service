namespace RentACar.Application.Branches;

/// <summary>Şube oluşturma/güncelleme giriş modeli.</summary>
public sealed class BranchInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Eposta { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Yetkili { get; set; }
    public string? CalismaSaatleri { get; set; }
    public decimal? KomisyonOran { get; set; }
    public string? EvrakNoOnek { get; set; }
    // ---- FAZ-23 şube derinliği ----
    public string? WebIsim { get; set; }
    public string? FirmaUnvani { get; set; }
    public int? WebRezOncesiSaat { get; set; }
    public decimal? Enlem { get; set; }
    public decimal? Boylam { get; set; }
    public decimal? HizmetKomisyonOran { get; set; }
    public string? RezervasyonRengi { get; set; }
    public bool AlisSubesiDegilMi { get; set; }
    public int? WebSira { get; set; }
    public string? WebOtoparkId { get; set; }
    public string? BayiCariKod { get; set; }
    public string? BayiOfisId { get; set; }
    public string? KomisyonHesabi { get; set; }
    public string? OnlineRezId { get; set; }
    public string? SozlesmeNoFormati { get; set; }
    public Guid? NakitHesapId { get; set; }
    public Guid? BankaHesapId { get; set; }
    public string? EntegrasyonKodu { get; set; }
    public string? ResimDosyasi { get; set; }
    public string? HaftalikCalismaSaatleri { get; set; }

    public bool Aktif { get; set; } = true;
}

/// <summary>FAZ-23 — şubeye özel ücretsiz hizmet girişi.</summary>
public sealed class SubeUcretsizHizmetInput
{
    public Guid SubeId { get; set; }
    public string HizmetAdi { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
}

/// <summary>
/// FAZ-23 — şube birleştirme ÖNİZLEMESİ: hangi tabloda kaç kayıt taşınacak.
///
/// <para>Toplu UPDATE geri alınamaz; kullanıcı onaydan ÖNCE etkiyi görmeli. Sayılar
/// birleştirmeyle AYNI sorgudan üretilir — önizleme ile gerçek işlem ayrışamaz.</para>
/// </summary>
public sealed record SubeBirlestirOnizleme(
    string KaynakAd, string HedefAd, IReadOnlyList<(string Tablo, int Adet)> Etkilenen)
{
    public int Toplam => Etkilenen.Sum(x => x.Adet);
}
