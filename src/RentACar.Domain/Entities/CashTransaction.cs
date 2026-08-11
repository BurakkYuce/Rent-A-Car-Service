using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Nakit işlem (Tahsilat). Tenant-owned + auditable (kim/ne zaman). Defter kayıtlarını
/// (dengeli) ve —kira bağlıysa— sözleşme Tahsilat/Bakiye'sini üreten "belge". Düzeltme
/// ters kayıtla yapılır (yeni CashTransaction + ters defter kümesi).
/// </summary>
public class CashTransaction : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (TH-000001).</summary>
    public string No { get; set; } = string.Empty;

    public CashTransactionType Tip { get; set; } = CashTransactionType.Tahsilat;

    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Çok-dövizli tutar (tutar + döviz + kur). Bakiye base'e çevrilerek işlenir.</summary>
    public Money Amount { get; set; } = Money.Zero("TRY");

    /// <summary>Karşı hesap (PR #5: Kasa).</summary>
    public LedgerAccountType KarsiHesap { get; set; } = LedgerAccountType.Kasa;

    /// <summary>
    /// FAZ-50 — para hareketinin geçtiği SPESİFİK kasa/banka hesabı (<c>FinancialAccount</c>).
    /// <see cref="KarsiHesap"/> "Kasa mı Banka mı" sorusunu, bu alan "hangi kasa/banka" sorusunu
    /// yanıtlar; enum EMEKLİ EDİLMEDİ çünkü defter türü ondan okunur.
    /// <b>null = hesap belirtilmemiş</b> (bu fazdan önceki tüm kayıtlar ve hesap seçmeyen formlar).
    /// Defter satırının <c>AccountRef</c>'ine bu değer yazılır.
    /// </summary>
    public Guid? HesapId { get; set; }

    public string? Aciklama { get; set; }

    public bool TersKayitMi { get; set; }
    public Guid? TersAlinanId { get; set; }

    /// <summary>Toplu işlem idempotency anahtarı (parite #10). Dolu olduğunda tenant içinde benzersiz
    /// (kısmi unique index) → aynı toplu işlemin çift-submit'i çakışır, atomik batch geri alınır.</summary>
    public Guid? IslemAnahtari { get; set; }

    /// <summary>FAZ-84 — tahsilatın/ödemenin hangi kanaldan girildiği (raporlanabilir BİLGİ alanı;
    /// "Masaüstü"/"Mobil"/"Tablet" — bkz. <see cref="CashKanal"/>). SAF BİLGİDİR: <see cref="AccountLedgerEntry"/>
    /// şemasına/dengesine hiç girmez, cari bakiyeyi/kasa-banka özetini ETKİLEMEZ (KARARLAR.md FAZ-84).
    /// Nullable: bu alan eklenmeden ÖNCE yazılmış geçmiş kayıtlar NULL kalır (o dönemde tek kanal
    /// masaüstüydü — ekranda "Masaüstü" olarak gösterilir, ama DB'de gerçek/tahmini ayrımı NULL ile
    /// korunur; migration YENİDEN YAZMAZ). Yeni kayıtlarda CashService varsayılan "Masaüstü" atar.</summary>
    public string? Kanal { get; set; } = CashKanal.Masaustu;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>FAZ-84 — <see cref="CashTransaction.Kanal"/> için izin verilen değerler (canlı
/// mobil_odeme.aspx eşdeğeri: masaüstü/mobil/tablet tahsilat noktaları). Serbest metin DEĞİL —
/// <c>CashService</c> girişte bu kümeye karşı doğrular (bilinmeyen kanal gürültülü reddedilir).</summary>
public static class CashKanal
{
    public const string Masaustu = "Masaüstü";
    public const string Mobil = "Mobil";
    public const string Tablet = "Tablet";

    public static readonly IReadOnlyList<string> Hepsi = [Masaustu, Mobil, Tablet];

    /// <summary>Boş/whitespace → varsayılan (Masaüstü). Bilinen bir değere (case-insensitive) eşleşmezse
    /// null döner — çağıran gürültülü reddetsin (sessiz normalize yanlış raporlamaya yol açar).</summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Masaustu;
        var trimmed = raw.Trim();
        foreach (var k in Hepsi)
            if (string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)) return k;
        return null;
    }
}
