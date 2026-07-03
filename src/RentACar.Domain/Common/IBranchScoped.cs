namespace RentACar.Domain.Common;

/// <summary>
/// Şube serbest-metni + Branch FK'si taşıyan entity'ler (roadmap F1 şube-FK tamamlama). Yazımda
/// <see cref="BranchFkInterceptor"/> SubeAdi'nı tenant Branch master'ından çözüp SubeFk'yi doldurur
/// → SubeId DAİMA Sube metninin türevi (tek doğruluk kaynağı: metin; FK denormalize). Metin korunur
/// (additive); eşleşme yoksa SubeFk null. Vehicle/Expense/... `Sube`+`SubeId`, User `AtanmisSube`+
/// `AtanmisSubeId` → arayüz iki adı tek soyut çifte eşler.
/// </summary>
public interface IBranchScoped
{
    /// <summary>Şube serbest-metni (Branch.Ad ile case-insensitive eşleşir).</summary>
    string? SubeAdi { get; }

    /// <summary>Çözülen Branch FK'si (metin eşleşmezse null).</summary>
    Guid? SubeFk { get; set; }
}
