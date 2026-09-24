using RentACar.Application.Pricing;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.ServiceInsurance;

// F9.1 — fiyat hesapla / maliyet hesapla / maliyet teklifleri / tarife aktar DTOs. Every figure comes from the SERVER
// engine (RentalQuoteEngine, MaliyetHesapService); the SPA carries no formula. None of these write to the ledger.

/// <summary>Fiyat motoru v1 teklif isteği (salt hesap).</summary>
public sealed record PriceQuoteRequest(string? AracGrupKod, string? Kanal, string? Sube, DateTimeOffset? BasTar,
    DateTimeOffset? BitTar, int? SurucuYas, int? TahminiKm, IReadOnlyList<string>? SigortaUrunKodlari, string? MusteriSegment,
    string? KampanyaKodu);

public sealed record PriceQuoteLineDto(string Kod, string Ad, string Tur, decimal BirimUcret, int Gun, decimal Tutar);

public sealed record PriceQuoteDto(int Gun, int HediyeGun, int FaturalananGun, decimal GunlukUcret, decimal BazTutar,
    decimal HaftaSonuFark, decimal KmAsimTutar, decimal SigortaToplam, decimal AraToplam, decimal IskontoOran,
    decimal IskontoTutar, decimal GenelToplam, string ParaBirimi, string? TarifeKodu, decimal Provizyon, decimal Muafiyet,
    bool GencSurucu, IReadOnlyList<PriceQuoteLineDto> SigortaKalemleri, IReadOnlyList<string> Notlar)
{
    public static PriceQuoteDto From(QuoteResult q) => new(q.Gun, q.HediyeGun, q.FaturalananGun, q.GunlukUcret, q.BazTutar,
        q.HaftaSonuFark, q.KmAsimTutar, q.SigortaToplam, q.AraToplam, q.IskontoOran, q.IskontoTutar, q.GenelToplam, q.ParaBirimi,
        q.TarifeKodu, q.Provizyon, q.Muafiyet, q.GencSurucu,
        q.SigortaKalemleri.Select(l => new PriceQuoteLineDto(l.Kod, l.Ad, l.Tur.ToString(), l.BirimUcret, l.Gun, l.Tutar)).ToList(),
        q.Notlar);
}

/// <summary>Filo maliyet hesabı girdisi (oranlar KESİR: 0,30 = %30). Boş alan = servis varsayılanı.</summary>
public sealed record CostInputDto(decimal? AlisBedeli, decimal? ResidualYuzde, int? SureAy, decimal? FaizOran, decimal? KkdfOran,
    decimal? BsmvOran, decimal? DamgaOran, decimal? KarMarji, decimal? KdvOran, decimal? EnflasyonOran, string? KrediHesaplamaSekli,
    int? AracSayisi, decimal? KaskoYillik, decimal? TrafikSigortasiYillik, decimal? MtvYillik, decimal? BakimYillik,
    decimal? LastikYillik, decimal? LastikKisYillik, decimal? AracTakipYillik, decimal? TescilPlakaYillik,
    decimal? MuayeneEmisyonYillik, decimal? YedekAracYillik, decimal? YonetimGideriAylik, decimal? AylikGider,
    decimal? BankaDosyaDigerMasraf)
{
    public static CostInputDto From(MaliyetHesapInput x) => new(x.AlisBedeli, x.ResidualYuzde, x.SureAy, x.FaizOran, x.KkdfOran,
        x.BsmvOran, x.DamgaOran, x.KarMarji, x.KdvOran, x.EnflasyonOran, x.KrediHesaplamaSekli.ToString(), x.AracSayisi,
        x.KaskoYillik, x.TrafikSigortasiYillik, x.MtvYillik, x.BakimYillik, x.LastikYillik, x.LastikKisYillik, x.AracTakipYillik,
        x.TescilPlakaYillik, x.MuayeneEmisyonYillik, x.YedekAracYillik, x.YonetimGideriAylik, x.AylikGider, x.BankaDosyaDigerMasraf);
}

public sealed record CostItemDto(string Ad, string Periyot, decimal Birim, decimal DonemTutar);

/// <summary>Araç BAŞINA sonuç + filo toplamları (araç başı × adet) + kalem dökümü.</summary>
public sealed record CostResultDto(decimal ResidualDeger, decimal NetAmortisman, decimal FinansmanFaiz, decimal FinansmanVergi,
    decimal Damga, decimal ToplamGider, decimal ToplamMaliyet, decimal BasaBasAylik, decimal Kar, decimal TeklifNet,
    decimal TeklifAylikNet, decimal TeklifKdvli, int AracSayisi, decimal FiloToplamMaliyet, decimal FiloTeklifNet,
    decimal FiloTeklifAylikNet, decimal FiloTeklifKdvli, IReadOnlyList<CostItemDto> Kalemler)
{
    public static CostResultDto From(MaliyetHesapSonuc s) => new(s.ResidualDeger, s.NetAmortisman, s.FinansmanFaiz,
        s.FinansmanVergi, s.Damga, s.ToplamGider, s.ToplamMaliyet, s.BasaBasAylik, s.Kar, s.TeklifNet, s.TeklifAylikNet,
        s.TeklifKdvli, s.AracSayisi, s.FiloToplamMaliyet, s.FiloTeklifNet, s.FiloTeklifAylikNet, s.FiloTeklifKdvli,
        s.Kalemler.Select(k => new CostItemDto(k.Ad, k.Periyot.ToString(), k.Birim, k.DonemTutar)).ToList());

    /// <summary>Kayıtlı teklifin SNAPSHOT'ı (yeniden fiyatlama değil); kalemler kayıtlı girdiden.</summary>
    public static CostResultDto From(MaliyetTeklifi t, IReadOnlyList<MaliyetGiderKalem> items) => new(t.ResidualDeger,
        t.NetAmortisman, t.FinansmanFaiz, t.FinansmanVergi, t.Damga, t.ToplamGider, t.ToplamMaliyet, t.BasaBasAylik, t.Kar,
        t.TeklifNet, t.TeklifAylikNet, t.TeklifKdvli, t.AracSayisi, t.FiloToplamMaliyet, t.TeklifNet * t.AracSayisi,
        t.FiloTeklifAylikNet, t.FiloTeklifKdvli,
        items.Select(k => new CostItemDto(k.Ad, k.Periyot.ToString(), k.Birim, k.DonemTutar)).ToList());
}

public sealed record CostOfferRow(Guid Id, string KayitNo, string Baslik, string? Plaka, DateTimeOffset Tarih, Guid? CariId,
    string? CariAd, Guid? HazirlayanId, int AracSayisi, decimal TeklifAylikNet, decimal TeklifKdvli, decimal FiloTeklifAylikNet,
    decimal FiloTeklifKdvli);

public sealed record CostOfferList(Application.Common.Sayfa<CostOfferRow> Kayitlar, MaliyetTeklifiOzet Ozet);

public sealed record CostOfferDetail(CostOfferRow Teklif, string? Aciklama, CostInputDto Girdi, CostResultDto Sonuc, string? Surum);

/// <summary>Teklif kaydı: künye + hesap girdisi (sonuç istemciden ALINMAZ, sunucu hesaplar). PUT'ta <c>surum</c> zorunlu.</summary>
public sealed record CostOfferRequest(string? Baslik, string? Plaka, DateTimeOffset? Tarih, Guid? CariId, Guid? HazirlayanId,
    string? Aciklama, CostInputDto? Girdi, string? Surum = null);

/// <summary>Tarife aktar ekranı: satırlar + seçili kanalın silinebilir (BEKLİYOR) satır sayısı.</summary>
public sealed record RateImportView(Application.Common.Sayfa<RateMatrixDto> Satirlar, int Bekleyen, int Onayli, int? Silinecek);

public sealed record RateImportResult(int Eklenen, int Atlanan, int Hatali, IReadOnlyList<string> Hatalar);

public sealed record RateChannelDeleteRequest(string? Kanal);

public sealed record RateChannelDeleteResult(int Silinen);
