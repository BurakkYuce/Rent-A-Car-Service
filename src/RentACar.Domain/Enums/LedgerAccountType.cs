namespace RentACar.Domain.Enums;

/// <summary>Defter hesap türü (çift-taraflı muhasebe boyutu).</summary>
public enum LedgerAccountType
{
    Cari = 0,   // müşteri/tedarikçi (AccountRef = CariId)
    Kasa = 1,   // nakit kasa
    Banka = 2,  // banka
    Gelir = 3,  // gelir (satış)
    Kdv = 4     // KDV
}
