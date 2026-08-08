using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ofis/Lokasyon (alış-dönüş noktası) master kaydı. Tenant-owned + auditable. Şube'den AYRI:
/// bir şubede birden çok ofis olabilir (havalimanı, şehir merkezi…). Additive — rezervasyon/
/// teklif/kira formlarındaki serbest-metin <c>CikisOfisi</c>/<c>DonusOfisi</c> alanlarını
/// besleyen açılır liste kaynağıdır (FK değil; seçilen <see cref="Ad"/> metni yazılır).
/// </summary>
public class Location : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    public string? Adres { get; set; }
    public string? Telefon { get; set; }

    // ---- Derinlik (roadmap K3; additive, nullable) ----
    public string? Eposta { get; set; }
    /// <summary>Çalışma saatleri (serbest metin, ör. "07:00-23:00").</summary>
    public string? CalismaSaatleri { get; set; }
    /// <summary>Lokasyon teslim/alış ek ücreti (drop dışı, sabit lokasyon ücreti).</summary>
    public decimal? TeslimUcreti { get; set; }

    /// <summary>Opsiyonel şube bağı (serbest metin — geriye-uyum; görüntü/yedek).</summary>
    public string? Sube { get; set; }
    /// <summary>Şube FK (Branch master, roadmap F1; metin korunur).</summary>
    public Guid? SubeId { get; set; }

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }

    // ---- FAZ-22 derinlik (additive, hepsi nullable — mevcut kayıtlar etkilenmez) ----

    /// <summary>Web/yabancı müşteri için İngilizce ad.</summary>
    public string? IngilizceAd { get; set; }
    /// <summary>Buluşma noktası ("Ofis Teslim" / "Havalimanı Karşılama" / "Shuttle"…).</summary>
    public string? BulusmaNoktasi { get; set; }
    /// <summary>Havalimanı IATA kodu (ör. IST, SAW). Havalimanı olmayan ofiste boş.</summary>
    public string? Iata { get; set; }
    /// <summary>Halka açık sitede GİZLE — açılır listelerde görünmeye devam eder (operasyon kullanır).</summary>
    public bool WebdeGizle { get; set; }
    /// <summary>Lokasyon türü ("Havalimanı" / "Şehir Merkezi" / "Otel"…).</summary>
    public string? LokasyonTuru { get; set; }
    public string? BinaNo { get; set; }
    /// <summary>Yol tarifi (serbest metin).</summary>
    public string? Tarif { get; set; }
    public string? Ulke { get; set; }
    public string? PostaKodu { get; set; }
    /// <summary>Harita konumu — "enlem,boylam" ya da harita bağlantısı (serbest metin).</summary>
    public string? MapsKonumu { get; set; }
    public string? EkAciklama { get; set; }
    /// <summary>Halka açık sitede sıralama (küçük önce). Boş = ada göre.</summary>
    public int? WebSira { get; set; }
    /// <summary>Drop karşılama türü (bu ofise bırakışta karşılama biçimi).</summary>
    public string? DropKarsilamaTuru { get; set; }
    /// <summary>Drop çalışma şekli.</summary>
    public string? DropCalismaSekli { get; set; }
    public string? OzelMail { get; set; }
    public string? OzelTelefon { get; set; }

    /// <summary>
    /// Haftalık çalışma saatleri — 7 satır (Pzt…Paz), JSONB tek kolon. Canlıdaki 14 ayrı sütun
    /// yerine tek kolonda modellendi: gün eklemek/çıkarmak şema değişikliği gerektirmesin.
    /// Serbest metin <see cref="CalismaSaatleri"/> KORUNUR (geriye uyum; biri görüntü, biri yapılı veri).
    /// </summary>
    public List<GunSaat> HaftalikCalismaSaatleri { get; set; } = [];

    /// <summary>Pasif ofisler açılır listelerde gizlenir ama kayıtlar korunur.</summary>
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
