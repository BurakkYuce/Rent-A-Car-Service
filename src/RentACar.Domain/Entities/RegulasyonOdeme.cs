using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// MTV kısmi ödemesi (FAZ-14). Bir MTV kaydına birden çok ödeme yazılabilir; her ödeme KENDİ
/// dengeli defter çiftini postlar ve <see cref="MtvRecord.Kalan"/>'ı düşürür.
///
/// <para><b>NEDEN AYRI TABLO:</b> evrak no / işlem yapan / ödeme tarihi / kasa kodu bir ÖDEMENİN
/// özellikleridir, kaydın değil. Bunları MtvRecord'a koysaydık ikinci kısmi ödeme birincinin
/// evrak numarasını EZERDİ ve ödeme geçmişi kaybolurdu.</para>
///
/// <para><b>Defter bağı:</b> defter satırları <c>SourceType="MtvOdeme"</c>, <c>SourceId=</c>bu
/// satırın Id'si. Mevcut kısmi unique index <c>(TenantId, SourceType, SourceId, Direction)</c>
/// böylece DEĞİŞMEDEN çalışmaya devam eder: ödeme başına tam bir borç + bir alacak.</para>
///
/// <para><b>Değişmezlik:</b> mali belge — <c>racar_app</c>'e yalnız SELECT/INSERT verilir
/// (UPDATE/DELETE yetkisi YOK). Düzeltme yolu bu fazda YOK; ileride ters-kayıt deseniyle gelir.</para>
/// </summary>
public class MtvOdeme : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid MtvId { get; set; }

    /// <summary>Kayıt içindeki ödeme sırası (1'den başlar) — satır kilidi altında atanır.</summary>
    public int Sira { get; set; }

    /// <summary>Bu ödemenin tutarı (kaydın para birimi = TRY).</summary>
    public decimal Tutar { get; set; }

    /// <summary>Ödeme sonrası kalan bakiye — denetim için ANLIK GÖRÜNTÜ (kayıttan yeniden türetilmez).</summary>
    public decimal KalanSonrasi { get; set; }

    public DateTimeOffset Tarih { get; set; }

    /// <summary>Kasa mı banka mı (defterdeki alacak hesabı).</summary>
    public LedgerAccountType Hesap { get; set; }

    /// <summary>Spesifik kasa kodu / IBAN (canlı parite; serbest metin, defteri etkilemez).</summary>
    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }

    public string? EvrakNo { get; set; }
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }

    /// <summary>Çift-submit koruması (Expense deseni): kısmi unique index.</summary>
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Muayene kısmi ödemesi (FAZ-14). <see cref="MtvOdeme"/> ile aynı sözleşme; ek olarak
/// <see cref="Ceza"/> taşır — ceza ödeme anında girilir (mevcut akış korunuyor) ve BORCU ARTIRIR:
/// <c>Kalan = Kalan + Ceza − Tutar</c>. Defter tutarı yalnız <see cref="Tutar"/>'dır; ceza
/// borcu büyütür, kendiliğinden ödenmiş sayılmaz.
/// </summary>
public class MuayeneOdeme : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid InspectionId { get; set; }
    public int Sira { get; set; }
    public decimal Tutar { get; set; }
    /// <summary>Bu ödemede eklenen ceza (borcu artırır).</summary>
    public decimal Ceza { get; set; }
    public decimal KalanSonrasi { get; set; }

    public DateTimeOffset Tarih { get; set; }
    public LedgerAccountType Hesap { get; set; }
    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }
    public string? EvrakNo { get; set; }
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
