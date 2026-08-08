namespace RentACar.Application.Finance;

/// <summary>
/// Cari virman geçmişi satırı: künye (vade/makbuz/şube/işlem yapan) + DEFTERDEN okunan tutar.
///
/// <para><b>Tutar defterden gelir</b>, künye tablosundan DEĞİL: künye para taşımıyor. Böylece
/// listede görünen rakam ile carinin ekstresindeki rakam ayrışamaz.</para>
/// </summary>
public sealed record CariVirmanSatirDto(
    Guid Id, DateTimeOffset Tarih, DateTimeOffset? Vade,
    Guid KaynakCariId, string KaynakCariAd,
    Guid HedefCariId, string HedefCariAd,
    decimal Tutar, string Doviz, decimal Kur,
    string? MakbuzNo, string? Sube, string? IslemYapan, string? Aciklama)
{
    /// <summary>Tutarın TL karşılığı (çok-dövizli listede toplama için).</summary>
    public decimal TutarTl => Tutar * Kur;
}

/// <summary>Cari virman geçmişi filtresi. Boş filtre = tüm virmanlar.</summary>
public sealed class CariVirmanFilter
{
    /// <summary>Verilen cari KAYNAK ya da HEDEF olan virmanlar (tek kutuda "bu cariyle ilgili hepsi").</summary>
    public Guid? CariId { get; set; }
    /// <summary>Makbuz no / açıklama / şube içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    public int EnFazla { get; set; } = 1000;
}
