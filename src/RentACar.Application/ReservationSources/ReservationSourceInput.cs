using RentACar.Domain.Enums;

namespace RentACar.Application.ReservationSources;

/// <summary>Rezervasyon kaynağı oluştur/güncelle giriş modeli.</summary>
public sealed class ReservationSourceInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    /// <summary>Kaynağın arkasındaki tedarikçi/acente adı.</summary>
    public string? Tedarikci { get; set; }

    // FAZ-24 — YÜZDE (12,5 = %12,5). Bu alanlar yalnız SAKLANIR; hiçbir fiyat/komisyon hesabı
    // okumaz. Bir tüketici eklenmeden önce ayrı para incelemesi gerekir.
    public decimal? KiraOrani { get; set; }
    public decimal? HizmetOrani { get; set; }
    public decimal? DropOrani { get; set; }

    // ---- FAZ-49 kural matrisi -------------------------------------------------------------
    // KURAL BAYRAKLARI (uygulanır) ile BİLGİ ALANLARI (hesaba girmez) ayrımı entity'de
    // gerekçesiyle yazılıdır (ReservationSource.cs).

    public RezKaynakGrubu? KaynakGrubu { get; set; }

    // KURAL — gerçekten uygulanır
    public bool Uzatamaz { get; set; }
    public bool RezTarihleriDegisemez { get; set; }
    public bool ProvizyonYok { get; set; }
    public bool KmSinirsiz { get; set; }
    public bool AyniYonDrop { get; set; }
    public int? MaxGun { get; set; }

    // BİLGİ — yalnız saklanır
    public bool MaliyetYansitma { get; set; }
    public bool MatrisErken { get; set; }
    public bool MatrisGecikme { get; set; }
    public bool MatrisIptal { get; set; }
    public bool MatrisNoShow { get; set; }
    public bool MatrisUzatma { get; set; }

    public string? SigortaKaynakNo { get; set; }
    public string? DropKaynakNo { get; set; }
    public string? ProvizyonSecenek { get; set; }
    public string? MuafiyatSecenek { get; set; }

    public bool ScdwDahil { get; set; }
    public bool CdwDahil { get; set; }
    public bool LcfDahil { get; set; }
    public bool PaiDahil { get; set; }

    public decimal? BebekKoltugu { get; set; }
    public decimal? Navigasyon { get; set; }
    public decimal? EkSurucu { get; set; }
    public decimal? Wifi { get; set; }

    public decimal? KomisyonOrani { get; set; }
    public decimal? OnOdemeOrani { get; set; }
    public decimal? IndirimOrani { get; set; }
    public decimal? PuanOrani { get; set; }

    public string? MailAdres { get; set; }
    public bool OtomatikMailGitme { get; set; }
    public bool RiskAnalizYapma { get; set; }
    public bool SubeGor { get; set; }
    public bool AcenteFiyatDegistir { get; set; }
    public bool Gizle { get; set; }
    public bool SadeceMusteriOdeme { get; set; }
}
