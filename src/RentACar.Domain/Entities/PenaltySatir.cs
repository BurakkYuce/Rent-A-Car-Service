using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Trafik cezası KALEMİ (FAZ-60). Canlı TürevRent tek ceza kaydında 3 ayrı
/// <c>Ceza_Tutari1-3</c>/<c>Ceza_Sebebi1-3</c> satırı taşır; burada sınırsız alt-satır olarak
/// modellenir.
///
/// <para><b>KARARLAR.md FAZ-60 kararı — SATIR BAZINDA KALAN:</b> her satırın KENDİ
/// <see cref="Odenen"/>/<see cref="Kalan"/> değeri vardır. Bir ödeme daima TEK bir satıra
/// yazılır (<see cref="PenaltyOdeme.SatirId"/>), böylece "hangi satırın ne kadarı ödendi"
/// belirsizleşmez.</para>
///
/// <para><b>Değişmez tutulan yapısal davranış:</b> her cezanın EN AZ BİR satırı vardır —
/// satır verilmezse başlıktaki <c>Tutar</c>/<c>Sebep</c> tek satır olarak maddeleştirilir
/// (migration eski kayıtları da böyle doldurur). Bu sayede
/// <c>Penalty.Tutar == Σ Satır.Tutar</c> ve <c>Penalty.OdenenTutar == Σ Satır.Odenen</c>
/// değişmezleri KOŞULSUZ geçerlidir; "satırsız ceza" özel durumu hiç doğmaz.</para>
/// </summary>
public class PenaltySatir : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid PenaltyId { get; set; }

    /// <summary>Kayıt içindeki satır sırası (1'den başlar).</summary>
    public int Sira { get; set; }

    /// <summary>Satır tutarı (TRY). Pozitif olmalıdır (DB CHECK ile de zorlanır).</summary>
    public decimal Tutar { get; set; }

    public string? Sebep { get; set; }

    /// <summary>
    /// Bu satıra bugüne dek yazılmış ödemelerin toplamı — <c>Σ PenaltyOdeme.Tutar</c>'ın
    /// ÖNBELLEĞİ. Yetkili kaynak ödeme satırlarıdır; önbellek her ödemede aynı transaction
    /// içinde, danışma kilidi arkasında yeniden hesaplanır (drift imkânsız).
    /// </summary>
    public decimal Odenen { get; set; }

    /// <summary>Kalan borç = <c>Tutar − Odenen</c>. ASLA negatif olamaz (DB CHECK).</summary>
    public decimal Kalan { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Ceza kalemi ödemesi (FAZ-60). <see cref="MtvOdeme"/>/<see cref="MuayeneOdeme"/> ile AYNI
/// sözleşme (FAZ-14 deseni tekrarlanır, yeni bir kopya uydurulmaz).
///
/// <para><b>NEDEN AYRI TABLO:</b> makbuz no / işlem yapan / ödeme tarihi / kasa kodu bir
/// ÖDEMENİN özellikleridir. Başlığa koysaydık ikinci kısmi ödeme birincinin makbuz numarasını
/// EZER ve ödeme geçmişi kaybolurdu.</para>
///
/// <para><b>Defter bağı:</b> <c>SourceType="CezaOdeme"</c>, <c>SourceId=</c>bu satırın Id'si.
/// Mevcut kısmi unique index <c>(TenantId, SourceType, SourceId, Direction)</c> deseni ödeme
/// başına tam bir borç + bir alacak yazılmasını garanti eder. Kayıt cezanın DEVLETE ödenmesidir:
/// <c>Borç Gider(araç) / Alacak Kasa-Banka</c>. Müşteriye yansıtma (Borç Cari / Alacak Gelir)
/// AYRI ve bağımsızdır — ikisi birlikte cezanın hem maliyetini hem gelirini defterde
/// dengeler, çift-sayım üretmez.</para>
///
/// <para><b>Değişmezlik:</b> mali belge — <c>racar_app</c>'e yalnız SELECT/INSERT verilir,
/// ayrıca <c>rc_prevent_mutation()</c> trigger'ı owner'ı da durdurur. Düzeltme yolu ters
/// kayıttır (bu fazda kapsam dışı).</para>
/// </summary>
public class PenaltyOdeme : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid PenaltyId { get; set; }

    /// <summary>Ödemenin yazıldığı ceza kalemi (KARARLAR.md: satır bazında kalan).</summary>
    public Guid SatirId { get; set; }

    /// <summary>
    /// O SATIR için ödeme sırası (1'den başlar) — idempotency anahtarının MONOTON bileşeni.
    /// Danışma kilidi arkasında, ödeme satırları SAYILARAK atanır.
    /// </summary>
    public int Sira { get; set; }

    /// <summary>Bu ödemenin tutarı (TRY).</summary>
    public decimal Tutar { get; set; }

    /// <summary>Ödeme sonrası SATIR kalanı — denetim için anlık görüntü.</summary>
    public decimal KalanSonrasi { get; set; }

    public DateTimeOffset Tarih { get; set; }

    /// <summary>Kasa mı banka mı (defterdeki alacak hesabı).</summary>
    public LedgerAccountType Hesap { get; set; }

    /// <summary>
    /// Deterministik idempotency anahtarı: <c>ceza:{penaltyId}:satir:{satirId}:odeme:{sira}</c>
    /// (InvariantCulture). MONOTON bileşen <c>sira</c>'dır — değer/tarih anlık görüntüsü
    /// kullanılsaydı aynı gün aynı tutarlı iki MEŞRU ödeme çakışırdı
    /// (memory: "idempotency-anahtar-tasarimi").
    /// </summary>
    public string Anahtar { get; set; } = string.Empty;

    /// <summary>Form çift-submit koruması (Expense deseni): kısmi unique index.</summary>
    public Guid? IslemAnahtari { get; set; }

    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }
    public string? MakbuzNo { get; set; }
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
