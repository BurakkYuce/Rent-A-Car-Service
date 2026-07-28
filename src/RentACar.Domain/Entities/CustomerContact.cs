using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kurumsal cari yetkili kişisi (PR-E) — DEĞİŞKEN sayıda (TürevRent musteri_genel_liste paritesi). Parent
/// <see cref="Customer"/> silinince cascade düşer. Yalnız İLETİŞİM bilgisi (ad/telefon/mail/görev) → PII/şifreleme
/// YOK (TC/ehliyet gibi kimlik-no taşımaz); Yetkili1-3 flat kolonlarının yerine geçer (flat'ler deprecated).
/// ServiceLine deseni: <see cref="ITenantOwned"/>, tekil FK (MusteriId), audit-timestamp'siz.
/// </summary>
public class CustomerContact : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MusteriId { get; set; }

    /// <summary>Form-satır sırası (0-tabanlı) — Guid PK insertion order garanti etmediği için gösterim/round-trip
    /// sırası bu alanla sabitlenir (repo Include'da OrderBy(Sira)).</summary>
    public int Sira { get; set; }

    public string AdSoyad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public string? Mail { get; set; }
    public string? Gorev { get; set; }
}
