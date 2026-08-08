using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Personel (roadmap C1; canlı "personel" karşılığı). Master kayıt; doğal anahtar = Kod (sicil).
/// PII alanları (<see cref="TcKimlikEnc"/>, <see cref="MaasEnc"/>) at-rest ŞİFRELİ cipher saklanır
/// (servis ISecretProtector ile yazar/okur) — D1 deseni. TcKimlik benzersizliği YOK (cipher non-deterministik).
/// Sube serbest metin (Branch master ile additive tutarlı).
/// </summary>
public class Personel : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Soyad { get; set; } = string.Empty;
    public string? TcKimlikEnc { get; set; }   // PII — şifreli
    public DateTimeOffset? IseGiris { get; set; }
    public DateTimeOffset? IseCikis { get; set; }
    public string? SurucuBelgeNo { get; set; }
    public string? MaasEnc { get; set; }       // PII — şifreli
    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK (roadmap F1; metin korunur)

    // ---- FAZ-40 derinlik (additive, hepsi nullable — mevcut kayıtlar etkilenmez) ----

    /// <summary>
    /// Görev tanımı ("İç Ekip" / "Yönetici" / "Operasyon" …). Personelin unvan/departman ayrımı
    /// bugüne kadar hiç yoktu; bu fazın en yüksek iş değeri taşıyan alanı.
    /// SERBEST METİN — sistem rolü (<see cref="Domain.Enums.UserRole"/>) DEĞİLDİR ve yetki VERMEZ.
    /// Yetki yalnız kullanıcı hesabından gelir; ikisini karıştırmak sessiz bir yetki yükseltme
    /// beklentisi yaratırdı.
    /// </summary>
    public string? GorevTanimi { get; set; }

    public string? Adres { get; set; }
    public string? EvTelefonu { get; set; }
    public string? IsTelefonu { get; set; }
    public string? CepTel { get; set; }
    public string? MailAdresi { get; set; }
    public string? Referans { get; set; }
    public string? Aciklama { get; set; }

    /// <summary>Sürücü belgesi sınıfı (B, C, E…).</summary>
    public string? SSinifi { get; set; }
    public DateTimeOffset? SVerilisTarihi { get; set; }
    public string? SVerilisYeri { get; set; }

    public DateTimeOffset? DogumTarihi { get; set; }
    public string? DogumYeri { get; set; }
    public string? BabaAdi { get; set; }
    public string? AnaAdi { get; set; }

    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Mahalle { get; set; }
    public string? CiltNo { get; set; }
    public string? AileSiraNo { get; set; }
    public string? SiraNo { get; set; }

    public string? KanGrubu { get; set; }
    /// <summary>Zimmetli RAC tablet numarası (operasyonel takip).</summary>
    public string? RacTabletNo { get; set; }

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
