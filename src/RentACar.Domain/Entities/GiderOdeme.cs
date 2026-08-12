using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// FAZ-64 — bir giderin KISMİ ödeme kaydı (canlı <c>gider_islemleri.aspx</c> "Kalan" takibi).
///
/// <para><b>Neden ayrı tablo:</b> <see cref="Expense"/> DB-DEĞİŞMEZDİR (<c>expenses_immutable</c>
/// BEFORE UPDATE OR DELETE trigger). Gider satırına "Odenen" kolonu eklemek işe yaramazdı — bir daha
/// asla güncellenemezdi. Ödemeler, MTV/muayene (FAZ-14) ve trafik cezası (FAZ-60) ile AYNI desende
/// append-only satırlar olarak tutulur; <c>Kalan</c> her okumada toplamdan türetilir.</para>
///
/// <para><b>DEFTERE YAZMAZ (KARARLAR.md FAZ-64 kararı):</b> gider ilk girişte TAM tutarıyla zaten
/// postlanmıştır. Burası yalnız TAKİP'tir: "bu borcun ne kadarı kapandı". Gerçek nakit çıkışı açık
/// hesap giderinde tedarikçiye yapılan <c>CashService.PayAsync</c> ile yürür; bu tabloya defter
/// bağlamak o hareketi İKİNCİ kez saydırırdı.</para>
///
/// <para>Bedeli (bilinçli): kasadan çıkış anı defterde ayrı görünmez.</para>
/// </summary>
public class GiderOdeme : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid ExpenseId { get; set; }
    /// <summary>Bu gider için kaçıncı ödeme (1'den başlar) — deterministik anahtarın MONOTON bileşeni.</summary>
    public int Sira { get; set; }

    /// <summary>Ödenen tutar (belgenin para biriminde; gider tek dövizlidir).</summary>
    public decimal Tutar { get; set; }
    /// <summary>Bu ödemeden SONRAKİ kalan — okuma kolaylığı; otorite Σ Tutar'dır.</summary>
    public decimal KalanSonrasi { get; set; }

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? MakbuzNo { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>İşlemi yapan — oturumdan alınır, formdan DEĞİL.</summary>
    public string? IslemYapan { get; set; }

    /// <summary>Deterministik idempotency anahtarı: <c>gider:{expenseId}:odeme:{sira}</c>.</summary>
    public string Anahtar { get; set; } = string.Empty;
    /// <summary>Form çift-submit koruması (her render'da yeni GUID).</summary>
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
