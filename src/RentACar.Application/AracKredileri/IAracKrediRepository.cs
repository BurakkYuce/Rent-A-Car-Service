using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public interface IAracKrediRepository
{
    Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default);
    /// <summary>FAZ-13 — Cari/Plaka/Dosya/Durum/Tarih aralığı filtresi. Plaka Vehicles'a JOIN gerektirir
    /// (kredide plaka kolonu yok, VehicleId var) → SQL'de alt-sorguyla süzülür.</summary>
    Task<IReadOnlyList<AracKredi>> SearchAsync(AracKrediFilter filtre, CancellationToken ct = default);
    Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracKredi row, CancellationToken ct = default);
    /// <summary>Taksit öde — FOR UPDATE satır kilidi altında sayaç + (FAZ 1.3) taksit GİDERİ aynı tx'te:
    /// <paramref name="posting"/> ödenen SIRA ile çağrılır, dönen Expense+dengeli defter çifti yazılır
    /// (ExpenseNo tahsisli). IslemAnahtari çift-submit'i kısmi unique index yakalar → ValidationException.</summary>
    Task<bool> TaksitOdeAsync(Guid id,
        Func<int, (Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries)>? posting = null,
        CancellationToken ct = default, Guid? islemAnahtari = null);
    Task<bool> SetDurumAsync(Guid id, KrediDurum durum, CancellationToken ct = default);

    /// <summary>FAZ-13 — toplu "Taksitleri İptal Et": yalnız <see cref="KrediDurum.Aktif"/> krediler
    /// iptale çevrilir; kontrol satır kilidinin ARKASINDA yapılır (ödeme yarışında kapanmış krediyi
    /// iptale düşürmemek için). Dönüş = gerçekten iptal edilen adet.</summary>
    Task<int> TopluIptalAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
