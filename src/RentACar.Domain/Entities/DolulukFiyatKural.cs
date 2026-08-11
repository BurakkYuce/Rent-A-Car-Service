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
public class DolulukFiyatKural : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Araç grubu kapsamı (null = tüm gruplar).</summary>
    public string? AracGrupKod { get; set; }

    /// <summary>FAZ-73 — şube serbest-metni (BranchFkInterceptor <see cref="SubeId"/>'yi bundan çözer).
    /// TEK BAŞINA hiçbir şey yapmaz: kural ancak <see cref="SadeceKendiSubeleri"/> true iken şubeye
    /// kısıtlanır (aşağıya bak). Böylece mevcut kayıtların davranışı bit-birebir korunur.</summary>
    public string? Sube { get; set; }

    /// <summary>Çözülen Branch FK'si (roadmap F1 deseni; metin tek doğruluk kaynağı, FK denormalize).</summary>
    public Guid? SubeId { get; set; }

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }

    /// <summary>
    /// FAZ-73 — true ise çarpan YALNIZ <see cref="Sube"/> şubesinden çıkan kiralara uygulanır;
    /// başka şubenin (ya da şubesi belirtilmemiş) teklifinde kural ADAY OLMAKTAN ÇIKAR.
    ///
    /// <para><b>Varsayılan false = ŞUBE-AGNOSTİK</b> — göç öncesi davranışın birebir aynısı. Bu
    /// bayrak açılmadıkça <see cref="Sube"/> alanı motorda hiç okunmaz; dolayısıyla mevcut
    /// kuralların ürettiği fiyat DEĞİŞMEZ (<c>FiyatMotoruYuzeyTests</c> kırılgan regresyonla kilitler).</para>
    ///
    /// <para>Servis doğrulaması: true iken <see cref="Sube"/> zorunludur (şubesiz "sadece kendi
    /// şubesi" kuralı hiçbir teklife uymaz — sessizce ölü kural olurdu).</para>
    /// </summary>
    public bool SadeceKendiSubeleri { get; set; }

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
