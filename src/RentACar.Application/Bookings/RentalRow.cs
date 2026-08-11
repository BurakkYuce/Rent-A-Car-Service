using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>Kira listesi satırı: sözleşme + müşteri/araç adı + fatura durumu (salt-okunur projeksiyon).</summary>
public sealed class RentalRow
{
    public Guid Id { get; init; }
    public string SozlesmeNo { get; init; } = string.Empty;
    public Guid MusteriId { get; init; }          // hızlı tahsilat formu (cariId) için
    public string MusteriAd { get; init; } = string.Empty;
    public string? Doviz { get; init; }           // kira dövizi — tahsilat kira dövizinde olmalı (K2)
    public string Plaka { get; init; } = string.Empty;
    public DateTimeOffset BasTar { get; init; }
    public DateTimeOffset BitTar { get; init; }
    public int Gun { get; init; }
    public decimal Tutar { get; init; }
    public decimal Bakiye { get; init; }
    public RentalStatus Durum { get; init; }
    public bool Faturali { get; init; }

    // ---- FAZ-46: canlı kira_listesi.aspx kolonları ----
    // Hepsi RentalContract'ta ZATEN VARDI ama projeksiyona yansımıyordu (ekranda görünmüyordu).
    // Tutar alanları BİLGİ: Provizyon/Depozito/Komisyon deftere ve bakiyeye yansımaz
    // (bkz. RentalContract'taki "BİLGİ AMAÇLI" notu) — liste de onları toplamaz, satırda gösterir.
    public string? Kaynak { get; init; }
    public decimal? Provizyon { get; init; }
    public decimal? Depozito { get; init; }
    public decimal? KomisyonOran { get; init; }
    public decimal? KomisyonTutar { get; init; }
    public DateTimeOffset? VadeTar { get; init; }
    public string? OnayKodu { get; init; }
    public string? ProjeAdi { get; init; }
    public string? AssistFirma { get; init; }
    public string? OzelSoforBilgisi { get; init; }
    public int? HediyeGun { get; init; }
    public int? FaturalananGun { get; init; }
    public string? CikisOfisi { get; init; }
    public string? DonusOfisi { get; init; }
}
