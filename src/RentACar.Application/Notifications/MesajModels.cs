using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>Şablon yaz modeli (tür + kanal doğal anahtar).</summary>
public sealed class MesajSablonInput
{
    public MesajTuru Tur { get; set; }
    public MesajKanal Kanal { get; set; }
    public string? Konu { get; set; }
    public string Govde { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}

public sealed record MesajSablonRow(
    Guid Id, MesajTuru Tur, MesajKanal Kanal, string? Konu, string Govde, bool Aktif);

public sealed record GidenMesajRow(
    Guid Id, string Tur, MesajKanal Kanal, string Alici, string? Konu,
    GidenMesajDurum Durum, string? Hata, int DenemeSayisi,
    DateTimeOffset OlusturmaUtc, DateTimeOffset? GonderimUtc, string? KaynakTur, Guid? KaynakId);

/// <summary>Giden mesaj listesi süzgeci (operatör "gitti mi" ekranı).</summary>
public sealed class GidenMesajFilter
{
    public GidenMesajDurum? Durum { get; set; }
    public MesajKanal? Kanal { get; set; }
    public string? Tur { get; set; }
    public int Take { get; set; } = 100;
}

/// <summary>
/// Bir bildirim gönderim isteği. <paramref name="Anahtar"/> DETERMİNİSTİKTİR ve idempotency'yi
/// taşır: tek seferlik olayda kaynak id yeter (<c>rez-onay:{id}</c>), tekrarlayan olayda anahtara
/// gün bileşeni girer (<c>iade-hatirlatma:{id}:2026-08-17</c>) — aksi halde ertesi gün gönderim
/// "zaten var" diye atlanırdı.
/// </summary>
public sealed record MesajIstegi(
    MesajTuru Tur,
    MesajKanal Kanal,
    string Alici,
    string Anahtar,
    IReadOnlyDictionary<string, string?> Degerler,
    string? KaynakTur = null,
    Guid? KaynakId = null);

/// <summary>Gönderim sonucu — çağıran akış bunu loglar/gösterir, ASLA yutmaz.</summary>
public sealed record MesajSonuc(GidenMesajDurum Durum, string? Hata)
{
    public bool Gonderildi => Durum == GidenMesajDurum.Gonderildi;
}

public interface IMesajRepository
{
    Task<IReadOnlyList<MesajSablonRow>> SablonListAsync(CancellationToken ct = default);
    Task<MesajSablon?> SablonBulAsync(MesajTuru tur, MesajKanal kanal, CancellationToken ct = default);
    Task SablonUpsertAsync(MesajSablonInput input, CancellationToken ct = default);

    /// <summary>Anahtarı olan kaydı döner (idempotency kontrolü).</summary>
    Task<GidenMesaj?> MesajBulAsync(string anahtar, CancellationToken ct = default);

    /// <summary>
    /// Kaydı ekler. Aynı anahtar zaten varsa (yarış) <c>false</c> döner ve HİÇBİR ŞEY yazmaz —
    /// benzersiz index ihlali yutulur.
    /// </summary>
    Task<bool> MesajEkleAsync(GidenMesaj mesaj, CancellationToken ct = default);

    Task MesajGuncelleAsync(Guid id, Action<GidenMesaj> apply, CancellationToken ct = default);

    Task<IReadOnlyList<GidenMesajRow>> MesajListAsync(GidenMesajFilter? filter = null, CancellationToken ct = default);
}
