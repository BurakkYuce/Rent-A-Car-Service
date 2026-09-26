using RentACar.Domain.Enums;

namespace RentACar.Application.Vehicles;

/// <summary>
/// FAZ-11 — canlı <c>arac_listesi.aspx</c>'in "Grup Türü" anahtarı: aynı arama kutusu ya araç
/// GRUBUNU ya da SIPP/ACRISS kodunu hedefler. İki ayrı kutu koymak yerine anahtar kullanılıyor
/// çünkü kullanıcı ikisini aynı anda aramıyor; canlıdaki davranış da bu.
/// </summary>
public enum VehicleGroupType
{
    Grup = 0,
    Sipp = 1
}

/// <summary>
/// FAZ-11 — canlı "ArTarih_Listesi": tarih aralığının HANGİ tarihe uygulanacağı. <c>Yok</c> iken
/// aralık hiç uygulanmaz (tarih kutuları dolu olsa bile) — "hangi tarih?" sorusu yanıtsızken
/// rastgele bir kolona uygulamak sessizce yanlış liste üretirdi.
/// </summary>
public enum VehicleDateType
{
    Yok = 0,
    FiloGiris = 1,
    FiloCikis = 2,
    Tescil = 3
}

/// <summary>
/// FAZ-11 — "Araç Sahibi" filtresinin ÖZEL kovaları. Belirli bir sahibi seçmek için
/// <see cref="VehicleFilter.AracSahibi"/> kullanılır; bu enum yalnız "hepsi / sahibi girilmemiş"
/// ayrımını taşır.
///
/// <para><b>Neden "Bizim / Dış" anahtarı YOK:</b> ilk tasarım "AracSahibi DOLU ise dış sahipli"
/// kuralını kullanıyordu. Canlı veriye bakınca çürüdü — kayıtların tamamında <c>AracSahibi</c>
/// alanı, değeri düpedüz <c>"Bizim"</c> olan bir metinle doluydu. O kural, kendi filosunun
/// tamamını "dış sahipli" gösterirdi. Mülkiyeti işaretleyen ayrı bir alan olmadığı sürece
/// doğru olan, sahibi TAHMİN etmek değil kullanıcıya seçtirmektir.</para>
/// </summary>
public enum VehicleOwnership
{
    Hepsi = 0,
    /// <summary>Yalnız araç sahibi hiç girilmemiş kayıtlar (veri eksiği avı).</summary>
    Girilmemis = 1
}

/// <summary>Araç liste arama/filtre + sayfalama. Sube servis tarafından şube kapsamına ayarlanır.</summary>
public sealed class VehicleFilter
{
    public string? Query { get; set; }       // plaka/marka (içeren, case-insensitive)
    public VehicleStatus? Durum { get; set; }
    /// <summary>Grup ya da SIPP değeri — hangisi olduğu <see cref="GrupTuru"/> ile belirlenir.</summary>
    public string? Grup { get; set; }
    /// <summary>FAZ-11: <see cref="Grup"/> kutusunun hedefi (Grup ↔ SIPP).</summary>
    public VehicleGroupType GrupTuru { get; set; } = VehicleGroupType.Grup;
    public string? Sube { get; set; }         // UI şube filtresi (kullanıcı seçimi)
    /// <summary>Rol bazlı şube KAPSAMI (C3; servis ayarlar) — UI Sube filtresinden bağımsız zorlanır.</summary>
    public Authorization.BranchScope.BranchFilter Kapsam { get; set; }

    // ---- FAZ-11 tarih aralığı (tip seçimli) ----
    public VehicleDateType TarihTuru { get; set; } = VehicleDateType.Yok;
    /// <summary>Aralık başlangıç ANI (dahil) — normalde başlangıç gününün gece yarısı.</summary>
    public DateTimeOffset? TarihBas { get; set; }
    /// <summary>Aralık bitiş GÜNÜNÜN başlangıç anı. Sorgu <c>&lt; TarihBit + 1 gün</c> uygular,
    /// yani bitiş günü DAHİLDİR (kullanıcı "31.12'ye kadar" derken 31.12'yi de kastediyor).</summary>
    public DateTimeOffset? TarihBit { get; set; }

    /// <summary>FAZ-11: "sahibi girilmemiş" özel kovası (belirli sahip için <see cref="AracSahibi"/>).</summary>
    public VehicleOwnership Sahiplik { get; set; } = VehicleOwnership.Hepsi;
    /// <summary>FAZ-11: belirli araç sahibi (tam eşleşme, kırpılmış). <see cref="Sahiplik"/>
    /// <c>Girilmemis</c> iken yok sayılır — iki kova aynı anda anlamlı değil.</summary>
    public string? AracSahibi { get; set; }

    /// <summary>
    /// F6.1a — sunucu tarafı sıralama (uç katmanının BEYAZ LİSTE <c>SiralamaHaritasi</c>'ından; eşitlik bozucu dahil).
    /// Null → eski davranış (plaka artan). İstemci girdisi asla ifadeye çevrilmez.
    /// </summary>
    public Func<IQueryable<Domain.Entities.Vehicle>, IOrderedQueryable<Domain.Entities.Vehicle>>? Siralama { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
