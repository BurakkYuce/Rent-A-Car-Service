using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>Şablon yaz modeli (tür + kanal doğal anahtar).</summary>
public sealed class MesajSablonInput
{
    public MessageType Tur { get; set; }
    public MessageChannel Kanal { get; set; }
    public string? Konu { get; set; }
    public string Govde { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}

public sealed record MesajSablonRow(
    Guid Id, MessageType Tur, MessageChannel Kanal, string? Konu, string Govde, bool Aktif);

public sealed record GidenMesajRow(
    Guid Id, string Tur, MessageChannel Kanal, string Alici, string? Konu,
    OutgoingMessageStatus Durum, string? Hata, int DenemeSayisi,
    DateTimeOffset OlusturmaUtc, DateTimeOffset? GonderimUtc, string? KaynakTur, Guid? KaynakId);

/// <summary>Giden mesaj listesi süzgeci (operatör "gitti mi" ekranı).</summary>
public sealed class GidenMesajFilter
{
    public OutgoingMessageStatus? Durum { get; set; }
    public MessageChannel? Kanal { get; set; }
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
    MessageType Tur,
    MessageChannel Kanal,
    string Alici,
    string Anahtar,
    IReadOnlyDictionary<string, string?> Degerler,
    string? KaynakTur = null,
    Guid? KaynakId = null);

/// <summary>Gönderim sonucu — çağıran akış bunu loglar/gösterir, ASLA yutmaz.</summary>
public sealed record MesajSonuc(OutgoingMessageStatus Durum, string? Hata)
{
    public bool Gonderildi => Durum == OutgoingMessageStatus.Gonderildi;
}

public interface IMessageRepository
{
    Task<IReadOnlyList<MesajSablonRow>> ListTemplatesAsync(CancellationToken ct = default);
    Task<MesajSablon?> FindTemplateAsync(MessageType type, MessageChannel channel, CancellationToken ct = default);
    Task UpsertTemplateAsync(MesajSablonInput input, CancellationToken ct = default);

    /// <summary>Anahtarı olan kaydı döner (idempotency kontrolü).</summary>
    Task<GidenMesaj?> FindMessageAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Kaydı ekler. Aynı anahtar zaten varsa (yarış) <c>false</c> döner ve HİÇBİR ŞEY yazmaz —
    /// benzersiz index ihlali yutulur.
    /// </summary>
    Task<bool> AddMessageAsync(GidenMesaj message, CancellationToken ct = default);

    Task UpdateMessageAsync(Guid id, Action<GidenMesaj> apply, CancellationToken ct = default);

    Task<IReadOnlyList<GidenMesajRow>> ListMessagesAsync(GidenMesajFilter? filter = null, CancellationToken ct = default);
}
