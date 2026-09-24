namespace RentACar.Web.Api.FinansHub;

// F8.1a — dönem kapanışı, otomatik tahsilat ve kurlar sözleşmesi (tür adları İngilizce, JSON alanları Türkçe).

/// <summary>Mizan satırı (baz para). <c>hesap</c> LedgerAccountType adı.</summary>
public sealed record TrialBalanceRow(string Hesap, string Ad, decimal Borc, decimal Alacak, decimal Bakiye);

/// <summary>Dönem kapanışı ekranı: mevcut kilit + güncel mizan + kapanışın sıfırlayacağı Gelir/Gider önizlemesi.
/// Mizan bakiye toplamı 0 olmalıdır (çift-taraflı defter dengesi).</summary>
public sealed record PeriodCloseState(
    DateOnly? KapanisTarihi, IReadOnlyList<TrialBalanceRow> Mizan,
    decimal ToplamBorc, decimal ToplamAlacak, decimal ToplamBakiye,
    decimal Gelir, decimal Gider, decimal DonemSonucu);

/// <summary>Dönemi kapat: kapanış fişi (Gelir/Gider → Dönem Sonucu) + kilit. Tarih bugünden ileri olamaz.</summary>
public sealed record PeriodCloseRequest(DateOnly? KapanisTarihi);

public sealed record AutoCollectionCandidate(
    Guid KiraId, string SozlesmeNo, int DonemSira, DateTimeOffset DonemBas, DateTimeOffset DonemBit,
    Guid CariId, string CariAd, string? Sube, string Doviz, decimal KiraTutar, decimal CariBakiye);

public sealed record CurrencyTotal(string Doviz, decimal Toplam);

/// <summary>Otomatik tahsilat adayları. <c>jobAcik</c> yalnız bilgidir (gece job'ı); elle tetik ayardan bağımsızdır.
/// Toplamlar döviz kırılımlıdır (farklı dövizler toplanmaz).</summary>
public sealed record AutoCollectionList(
    bool JobAcik, IReadOnlyList<AutoCollectionCandidate> Adaylar, IReadOnlyList<CurrencyTotal> DovizToplamlari);

public sealed record AutoCollectionSelection(Guid KiraId, int DonemSira);

/// <summary>Seçili dönemleri kes (+ <c>tahsilat</c> ise dönem tahsilatını yaz). <c>hesap</c> "Kasa" | "Banka".</summary>
public sealed record AutoCollectionRequest(IReadOnlyList<AutoCollectionSelection>? Secim, bool Tahsilat, string? Hesap);

/// <summary>Sonuç: kesilen dönem ve yazılan tahsilat sayısı (idempotent yutulan sayılmaz) + atlananların TAMAMI.</summary>
public sealed record AutoCollectionResult(int Kesilen, int Tahsilat, IReadOnlyList<string> Atlananlar);

// ------------------------------------------------------------------ kurlar

public sealed record CbrtRate(
    string Kod, string? Ad, int Birim, decimal? ForexAlis, decimal? ForexSatis, decimal? EfektifAlis, decimal? EfektifSatis,
    DateOnly Tarih, bool SabitVar);

/// <summary>Firma sabit kuru (kod başına tek). Pencere gün granülünde (UTC takvim günü, dahil). <c>surum</c> PUT için.</summary>
public sealed record FixedRate(Guid Id, string Kod, decimal Kur, DateOnly? BasTar, DateOnly? BitTar, bool Aktif, string? Surum);

public sealed record RatesScreen(IReadOnlyList<CbrtRate> Tcmb, IReadOnlyList<FixedRate> Sabitler, IReadOnlyList<string> DovizKodlari);

public sealed record FixedRateCreateRequest(string? Kod, decimal Kur, DateOnly? BasTar = null, DateOnly? BitTar = null, bool Aktif = true);

/// <summary>Tam değiştirme: kod değişmez; <c>surum</c> zorunlu (uyuşmazlık 409 <c>cakisma</c>).</summary>
public sealed record FixedRateUpdateRequest(decimal Kur, DateOnly? BasTar, DateOnly? BitTar, bool Aktif, string? Surum);

/// <summary>TCMB yenileme: "guncellendi" (n döviz yazıldı) | "guncel" (son 30 dk'da çekilmiş) | "basarisiz".</summary>
public sealed record RatesRefreshResult(string Durum, int Adet);

public sealed record ConversionResult(decimal Tutar, string Kaynak, string Hedef, decimal Sonuc);
