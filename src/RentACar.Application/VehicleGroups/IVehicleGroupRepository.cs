using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleGroups;

/// <summary>PR-10 grup güncelleme sonucu: <paramref name="Bulundu"/> false ise kayıt yok;
/// <paramref name="TasinanArac"/> = Ad değiştiği için <c>Grup</c> değeri taşınan araç sayısı.</summary>
public readonly record struct GrupGuncellemeSonuc(bool Bulundu, int TasinanArac);

public interface IVehicleGroupRepository
{
    /// <summary>Tüm araç grupları (yönetim ekranı). Koda göre sıralı.</summary>
    Task<IReadOnlyList<VehicleGroup>> ListAsync(CancellationToken ct = default);

    /// <summary>Yalnız aktif gruplar (form açılır listesi kaynağı).</summary>
    Task<IReadOnlyList<VehicleGroup>> ListActiveAsync(CancellationToken ct = default);

    Task<VehicleGroup?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tenant içinde aynı kod (büyük/küçük harf duyarsız) başka kayıtta var mı?</summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>PR-10: aynı Ad (TÜRKÇE-duyarsız: "EKONOMİ" == "ekonomi") başka kayıtta var mı?
    /// Ad artık taşıyıcı kolondur (araç eşlemesi, cascade, vitrin hep Ad üstünden çalışır) → iki grubun
    /// aynı adı taşıması filolarını tek isim havuzunda birleştirirdi. Türkçe katlama SQL'e itilemediği
    /// için karşılaştırma bellekte yapılır (grup sayısı azdır).</summary>
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(VehicleGroup group, CancellationToken ct = default);

    /// <summary>Grubu yükler, <paramref name="apply"/>'ı uygular; <b>Ad değiştiyse</b> eski ada
    /// Türkçe-duyarsız eşleşen araçların <c>Grup</c> değerini AYNI SaveChanges içinde yeni ada taşır
    /// (rename cascade). Tek transaction olmasının sebebi: grup adı değişip araçlar taşınmazsa tüm
    /// filo sessizce eşleşmez hale gelir — bu PR'ın kapattığı bug'ın ta kendisi.</summary>
    Task<GrupGuncellemeSonuc> UpdateAsync(Guid id, Action<VehicleGroup> apply, CancellationToken ct = default);

    /// <summary>F11.1b — yukarıdakinin sürümlü hâli: satır kilidi + xmin karşılaştırması + cascade TEK işlemde;
    /// uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<GrupGuncellemeSonuc> UpdateAsync(Guid id, string? expectedVersion, Action<VehicleGroup> apply, CancellationToken ct = default);

    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);

    /// <summary>PR-10 eşleme aracı: kaynak <c>Grup</c> değerine (Türkçe-duyarsız) sahip araçları
    /// <paramref name="targetName"/>'a taşır; taşınan sayıyı döner. <paramref name="emptyOnes"/> true ise
    /// kaynak, değeri BOŞ (null/whitespace) olan araçlardır — bu, string sentinel ("(boş)") yerine ayrı
    /// bayrakla taşınır ki gerçekten "(boş)" yazan bir grup değeriyle karışmasın.</summary>
    Task<int> MoveGroupValueAsync(string? sourceValue, bool emptyOnes, string targetName, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
