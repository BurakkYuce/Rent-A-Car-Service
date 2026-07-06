namespace RentACar.Domain.Common;

/// <summary>
/// Kod+Ad(+Aktif) master/tanım varlıklarının ortak yüzeyi (marka, renk, yakıt türü, ülke…).
/// Generic master taban (MasterTanimService / MasterTanimRepository) bu arayüz üzerinden çalışır;
/// EF modelini DEĞİŞTİRMEZ (yalnız arayüz bildirimi). Ekstra alanı olan tanımlar
/// (ör. PenaltyType.VarsayilanTutar, InsuranceCompany.Telefon) bu aileye GİRMEZ.
/// <see cref="Id"/> generic tabanın Create dönüşü ve Find için gereklidir.
/// </summary>
public interface IMasterTanim
{
    Guid Id { get; set; }
    string Kod { get; set; }
    string Ad { get; set; }
    bool Aktif { get; set; }
    DateTimeOffset? UpdatedAtUtc { get; set; }
}
