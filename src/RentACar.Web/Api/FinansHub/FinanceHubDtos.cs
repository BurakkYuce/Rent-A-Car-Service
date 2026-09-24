namespace RentACar.Web.Api.FinansHub;

// F8.1a — /api/ui/v1/finans/* (finans ekranları, 1. yarı) sözleşmesi. Tür adları İngilizce, JSON alan adları Türkçe
// (CLAUDE.md §2 adlandırma kuralı). Para alanları sunucuda hesaplanır; SPA formül taşımaz.

// ------------------------------------------------------------------ kasa / banka

/// <summary>Kasa/banka özeti (baz para, ₺): giriş/çıkış/bakiye. Negatif bakiye mümkündür (bilinçli, guard yok).</summary>
public sealed record CashboxSummary(
    decimal KasaGiris, decimal KasaCikis, decimal KasaBakiye,
    decimal BankaGiris, decimal BankaCikis, decimal BankaBakiye);

/// <summary>Nakit işlem (tahsilat/ödeme) satırı. <c>cariAd</c> MusteriGorunumu kuralıyla; TC yok.</summary>
public sealed record CashTransactionRow(
    Guid Id, string No, DateTimeOffset Tarih, string Tip, bool TersKayit, Guid? TersAlinanId,
    string Hesap, Guid? HesapId, string? HesapAd, string Kanal,
    Guid CariId, string CariAd, string? CariKod, Guid? KiraId,
    decimal Tutar, string Doviz, decimal Kur, decimal TutarTl, string? Aciklama);

/// <summary>Sayfalı nakit işlem listesi (ortak <c>Sayfa</c> biçimi). <c>kesildi</c>: sunucu tavanına ulaşıldı —
/// daha eski kayıtlar tarih/metin süzgeciyle bulunur (toplam tavanla sınırlıdır).</summary>
public sealed record CashTransactionList(RentACar.Application.Common.Sayfa<CashTransactionRow> Liste, bool Kesildi);

/// <summary>Kasa↔banka / hesaplar arası virman satırı (tutar defterden; künye ayrı tablodan).</summary>
public sealed record CashTransferRow(
    Guid Id, DateTimeOffset Tarih, string KaynakTur, Guid? KaynakHesapId, string? KaynakHesapAd,
    string HedefTur, Guid? HedefHesapId, string? HedefHesapAd,
    decimal Tutar, string Doviz, decimal Kur, decimal TutarTl,
    string? MakbuzNo, string? Sube, string? IslemYapan, string? Aciklama, bool KunyeVar);

/// <summary>Kasa/banka virmanı. <c>kaynak</c>/<c>hedef</c>: "Kasa" | "Banka". Aynı türde iki hesap arası virmanda
/// iki hesap da zorunludur (servis kuralı). <c>kur</c> boş → bugünkü kur (TRY=1).</summary>
public sealed record CashTransferRequest(
    string? Kaynak, string? Hedef, decimal Tutar, Guid? KaynakHesapId = null, Guid? HedefHesapId = null,
    string? Doviz = null, decimal? Kur = null, string? MakbuzNo = null, string? Sube = null, string? Aciklama = null);

/// <summary>Yazılan kaydın kimliği (ledger-only işlemlerde işlem anahtarı; sessiz idempotent tekrarda AYNI id).</summary>
public sealed record CashOperationResult(Guid Id);

// ------------------------------------------------------------------ cari

/// <summary>Carinin güncel cari bakiyesi (Σ SignedBase; pozitif = müşteri borçlu) ve tutulan depozitosu.</summary>
public sealed record CustomerBalance(Guid CariId, string CariAd, decimal Bakiye, decimal DepozitoBakiye);

/// <summary>Bakiye düzeltme. <c>yon</c>: "Alacaklandir" (bakiye ↓) | "Borclandir" (bakiye ↑). Kasa/Banka'ya dokunmaz.</summary>
public sealed record BalanceAdjustmentRequest(
    Guid CariId, string? Yon, decimal Tutar, string? Doviz = null, decimal? Kur = null,
    DateTimeOffset? Tarih = null, DateTimeOffset? Vade = null, string? MakbuzNo = null, string? Aciklama = null);

/// <summary>Cari↔cari virman: kaynak cari alacaklanır (bakiye ↓), hedef cari borçlanır (bakiye ↑).</summary>
public sealed record CustomerTransferRequest(
    Guid KaynakCariId, Guid HedefCariId, decimal Tutar, string? Doviz = null, decimal? Kur = null,
    DateTimeOffset? Tarih = null, DateTimeOffset? Vade = null, string? MakbuzNo = null, string? Sube = null,
    string? Aciklama = null);

public sealed record CustomerTransferRow(
    Guid Id, DateTimeOffset Tarih, DateTimeOffset? Vade,
    Guid KaynakCariId, string KaynakCariAd, Guid HedefCariId, string HedefCariAd,
    decimal Tutar, string Doviz, decimal Kur, decimal TutarTl,
    string? MakbuzNo, string? Sube, string? IslemYapan, string? Aciklama);

/// <summary>Cari ekstre satırı. <c>borc</c>/<c>alacak</c> baz para (₺); <c>yuruyen</c> sunucuda hesaplanır (devirden
/// başlar). <c>kasaIslemId</c> yalnız Tahsilat/Ödeme satırlarında dolu (ters kayıt ucunun kimliği).</summary>
public sealed record CustomerStatementLine(
    Guid Id, DateTimeOffset Tarih, string? Aciklama, string Kaynak, Guid KaynakId,
    string Doviz, decimal Tutar, decimal Kur, decimal Borc, decimal Alacak, decimal Yuruyen, Guid? KasaIslemId);

/// <summary>Özet görünüm: ay (İstanbul) × kaynak. Detayla aynı veri; toplamlar birebir eşit.</summary>
public sealed record CustomerStatementSummaryLine(string Ay, string Kaynak, int Adet, decimal Borc, decimal Alacak, decimal Net);

/// <summary>Cari ekstre. <c>bakiye</c> daima filtresiz gerçek bakiyedir. <c>yuruyenBakiyeMi</c> false ise (döviz/kaynak/
/// kira durumu süzgeci) son kolon bakiye DEĞİL görünen hareketlerin birikimidir.</summary>
public sealed record CustomerStatement(
    Guid CariId, string CariAd, decimal Bakiye, decimal Devir, bool YuruyenBakiyeMi,
    IReadOnlyList<CustomerStatementLine> Satirlar, IReadOnlyList<CustomerStatementSummaryLine>? Ozet,
    decimal ToplamBorc, decimal ToplamAlacak,
    IReadOnlyList<string> Dovizler, IReadOnlyList<string> Kaynaklar);

/// <summary>Tek cari toplu kapatma için borç kalemi. <c>kalan</c> = baz − kapanan (sunucu); <c>kapali</c> kalan ≤ 0,005.</summary>
public sealed record CustomerOpenItem(
    Guid Id, DateTimeOffset Tarih, string Kaynak, string? Aciklama, decimal Tutar, string Doviz,
    decimal Baz, decimal Kapanan, decimal Kalan, bool Kapali);

public sealed record CustomerOpenItems(Guid CariId, string CariAd, decimal Bakiye, decimal AcikToplam, IReadOnlyList<CustomerOpenItem> Kalemler);

/// <summary>Kapatılacak kalem. <c>tutar</c> boş → kalemin KALANI (kuruşa aşağı yuvarlanır).</summary>
public sealed record CloseItemSelection(Guid KalemId, decimal? Tutar = null);

public sealed record CloseItemsRequest(
    IReadOnlyList<CloseItemSelection>? Secim, string? Hesap, string? Kanal = null,
    DateTimeOffset? Tarih = null, string? Aciklama = null);

/// <summary>Toplu kapatma sonucu: tek tahsilat belgesi (baz para, TRY).</summary>
public sealed record CloseItemsResult(Guid Id, string BelgeNo, decimal Tutar);

/// <summary>Çok cari toplu tahsilat satırı (TRY). Bir satır geçersizse HİÇBİRİ yazılmaz.</summary>
public sealed record BulkCollectionLine(Guid CariId, decimal Tutar, string? Aciklama = null);

public sealed record BulkCollectionRequest(
    IReadOnlyList<BulkCollectionLine>? Satirlar, string? Hesap, Guid? HesapId = null, string? Kanal = null);

public sealed record BulkPostingResult(int Adet, decimal Toplam);

// ------------------------------------------------------------------ gider / depozito

/// <summary>Toplu gider kalemi: net tutar (TRY) + isteğe bağlı açıklama ve araç.</summary>
public sealed record BulkExpenseLine(decimal NetTutar, string? Aciklama = null, Guid? AracId = null);

/// <summary>Toplu gider. <c>tip</c> ExpenseType adı; <c>odemeYontemi</c> "Nakit" | "Banka" | "AcikHesap";
/// <c>kdvOrani</c> kesir (0,20 = %20).</summary>
public sealed record BulkExpenseRequest(
    IReadOnlyList<BulkExpenseLine>? Satirlar, string? Tip, string? OdemeYontemi, decimal KdvOrani,
    Guid? CariId = null, DateTimeOffset? Vade = null, Guid? FinansalHesapId = null);

public sealed record DepositBalanceRow(Guid CariId, string CariAd, decimal Bakiye);

/// <summary>Depozito iade (nakit çıkışı). Tutulanı aşamaz.</summary>
public sealed record DepositRefundRequest(
    Guid CariId, decimal Tutar, string? Hesap, Guid? HesapId = null, string? Doviz = null, decimal? Kur = null);

/// <summary>Depozito mahsup (cari borcuna). Tutulanı aşamaz; nakit hesap kullanılmaz.</summary>
public sealed record DepositOffsetRequest(Guid CariId, decimal Tutar, string? Doviz = null, decimal? Kur = null);
