namespace RentACar.Domain.Enums;

/// <summary>Gelen e-Fatura triage durumu. Beklemede → Onaylandı/Reddedildi; Onaylandı → İşlendi.</summary>
public enum IncomingEInvoiceStatus
{
    Beklemede = 0,
    Onaylandi = 1,
    Reddedildi = 2,
    Islendi = 3
}
