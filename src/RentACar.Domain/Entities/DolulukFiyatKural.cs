using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Doluluk-bazlı dinamik fiyat çarpanı kuralı (FAZ 3.A7). RentalRule DEĞİL — kural-seçimi müşteri
/// lehine en-avantajlıyı seçtiğinden surge oraya konamaz (her indirime yenilir/indirimi ezerdi);
/// çarpan BAĞIMSIZ aşamadır: motorda ResolveTierRate SONRASI, hediye/iskonto ÖNCESİ uygulanır
/// (iskonto matrahı surge'lü baz). Yalnız MOTOR yolu (manuel fiyat asla). CarpanYuzde 0..50:
/// uygulama kemeri (min 50) + DB CHECK (pantolon askısı — şemadaki İLK CHECK constraint, migration'da
/// ELLE SQL). Rezervasyon-UPDATE reprice'ında surge ATLANIR (müşteriye verilen fiyat sıçramasın).
/// </summary>
public class DolulukFiyatKural : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Araç grubu kapsamı (null = tüm gruplar).</summary>
    public string? AracGrupKod { get; set; }

    /// <summary>Doluluk eşiği % (1..100) — grup doluluğu bu değerin ÜZERİNDE/EŞİT ise kural aday.</summary>
    public int EsikYuzde { get; set; }

    /// <summary>Fiyat çarpanı % (0..50, DB CHECK'li sert tavan) — günlük ücrete +% uygulanır.</summary>
    public decimal CarpanYuzde { get; set; }

    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
