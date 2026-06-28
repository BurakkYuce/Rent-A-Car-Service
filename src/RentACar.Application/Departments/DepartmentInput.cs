namespace RentACar.Application.Departments;

/// <summary>Departman oluştur/güncelle giriş modeli.</summary>
public sealed class DepartmentInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
