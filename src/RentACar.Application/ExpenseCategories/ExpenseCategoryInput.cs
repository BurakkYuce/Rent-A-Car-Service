namespace RentACar.Application.ExpenseCategories;

/// <summary>Gider türü oluştur/güncelle giriş modeli.</summary>
public sealed class ExpenseCategoryInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;
}
