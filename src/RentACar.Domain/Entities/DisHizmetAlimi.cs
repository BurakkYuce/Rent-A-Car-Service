using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// B2B dış hizmet alımı (FAZ 4.3 — kullanıcı kararı: TAM DEFTERLİ). Kiraya bağlı, dış firmadan
/// alınan hizmetin (şoför, transfer, yıkama...) mali kaydı: hizmet bedeli GİDER (AccountRef =
/// kira aracı — karne/Karlilik gider tarafı) / tedarikçi CARİYE alacak; tedarikçi komisyonu
/// (bedel × oran) CARİYE borç / GELİR (karne atfı kira→araç — SourceType "DisHizmet").
/// DENGELİ set tek transaction; düzeltme TERS KAYITLA (Durum=Iptal + flip — silme yok).
/// Doviz/Kur 1.1 otomatiği (KurCozucu); No boşluksuz DH-. IslemAnahtari çift-submit çiti.
/// Bayi komisyon alanları + fatura no'ları + KdvMuaf/IndirimTuru TürevRent parite bilgi alanlarıdır.
/// </summary>
public class DisHizmetAlimi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (DH-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid RentalId { get; set; }
    /// <summary>Tedarikçi cari — hizmet borcu buna alacak, komisyon buna borç yazılır.</summary>
    public Guid FaturaKesilecekCariId { get; set; }
    /// <summary>Opsiyonel ikinci cari (bakiye takibi ayrı firmadaysa; bilgi).</summary>
    public Guid? BakiyeliCariId { get; set; }

    public string AlinanHizmet { get; set; } = string.Empty;
    public string? HizmetAlinanFirma { get; set; }

    /// <summary>Hizmet bedeli (belge/native döviz).</summary>
    public decimal HizmetBedeli { get; set; }
    /// <summary>Tedarikçi komisyon oranı % (0-100) — komisyon geliri = bedel × oran / 100.</summary>
    public decimal TedarikciKomisyonOran { get; set; }
    public decimal? BayiKomisyonOran { get; set; }
    /// <summary>BİLGİ alanı (parite) — defteri SÜRMEZ; komisyon defteri daima TedarikciKomisyonOran×bedel'den.</summary>
    public decimal? VerilecekKomisyonTutar { get; set; }
    public string? KomisyonFaturaNo { get; set; }
    public string? BayiFaturaNo { get; set; }
    public bool KdvMuaf { get; set; }
    public string? IndirimTuru { get; set; }

    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public DateTimeOffset Tarih { get; set; }
    public string? Aciklama { get; set; }

    public DisHizmetDurum Durum { get; set; } = DisHizmetDurum.Kayitli;

    /// <summary>Çift-submit çiti (form-başına token; kısmi unique).</summary>
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public enum DisHizmetDurum
{
    Kayitli = 0,
    Iptal = 1
}
