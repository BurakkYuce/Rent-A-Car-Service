using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tarife (fiyat) grubu master — canlı TürevRent <c>fiyat_grup_tanimlama.aspx</c> karşılığı.
/// Bir broker/kanal için ad + oran + (varsa) giriş kimliği tutar.
///
/// <para><b>Bu sürümde YALNIZ MASTER KAYITTIR.</b> Kimlik alanları saklanır ama hiçbir kimlik
/// doğrulama akışına BAĞLI DEĞİLDİR — broker'ın bu kimlikle XML/feed üzerinden sisteme girmesi
/// ayrı ve daha büyük bir iştir (yeni dikey) ve hangi feed/protokolün kullanılacağı belirlenmeden
/// açılmaz.</para>
///
/// <para><b>Şifre düz metin SAKLANMAZ:</b> yalnız <see cref="SifreHash"/> tutulur (tek yönlü;
/// kullanıcı/personel şifre deseniyle aynı <c>IPasswordHasher</c>). Geri okunamaz — unutulan şifre
/// sıfırlanır, gösterilmez.</para>
/// </summary>
public class TarifeGrubu : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Gruba uygulanacak oran (ör. komisyon/indirim yüzdesi). Bilgi amaçlı — fiyat motoru
    /// bu değeri OKUMAZ; tarife matrisi kendi fiyatını taşır.</summary>
    public decimal Oran { get; set; }

    /// <summary>Broker giriş kullanıcı adı (bilgi; doğrulama akışı yok).</summary>
    public string? KullaniciAdi { get; set; }
    /// <summary>Şifrenin tek yönlü özeti. Ham şifre HİÇBİR ZAMAN saklanmaz.</summary>
    public string? SifreHash { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
