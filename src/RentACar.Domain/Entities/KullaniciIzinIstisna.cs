namespace RentACar.Domain.Entities;

/// <summary>
/// Kullanıcı-bazlı izin istisnası (2026-08-17, kullanıcı kararı): rol matrisinin ÜSTÜNE tek
/// kullanıcıya ek izin verme (<see cref="Ver"/>=true) ya da matristen gelen bir izni o
/// kullanıcıdan geri alma (<see cref="Ver"/>=false). Yasak DAİMA kazanır (ek izinle çakışırsa).
///
/// <para><b>Neden platform tablosu (ITenantOwned DEĞİL):</b> istisnalar LOGIN SIRASINDA okunur
/// (claim'e yazılır) ve login anında tenant GUC'u henüz yoktur — merkezi tenant query-filter'ı
/// bu okumayı boş döndürürdü. <c>Users</c> ile aynı desen: açık <see cref="TenantId"/> kolonu,
/// repository'de elle tenant filtresi, komut-bazlı RLS (SELECT GUC-boşken açık — login bootstrap;
/// yazma daima tenant'a kısıtlı).</para>
///
/// <para><b>Etkinleşme:</b> istisna claim'e login'de yazıldığı için değişiklik kullanıcının BİR
/// SONRAKİ girişinde etkinleşir (UI bunu söyler). Servis guard'ı claim'den değil ICurrentUser'dan
/// okur; ICurrentUser web/api'de claim'den beslenir.</para>
/// </summary>
public class KullaniciIzinIstisna
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>İzin ADI (<c>Permission</c> enum adı, ör. "ViewReports"). Sayı değil AD saklanır:
    /// enum'a değer eklenince/sıra değişince kayıt anlamını korur; bilinmeyen ad guard'da yok sayılır.</summary>
    public string Izin { get; set; } = string.Empty;

    /// <summary>true = ek izin (grant), false = yasak (deny). Yasak, matristen ve ek izinden ÜSTÜNDÜR.</summary>
    public bool Ver { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Kim tanımladı (denetim izi — hangi admin verdi/kıstı).</summary>
    public string? TanimlayanKullanici { get; set; }
}
