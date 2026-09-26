namespace RentACar.Domain.Enums;

/// <summary>Fatura dönemi durumu (FAZ 4.2): Planlandi (kesilebilir/yeniden üretilebilir),
/// Kesildi (Invoice bağlı — dokunulmaz), Atlandi (cap nedeniyle kesilemedi — erken dönüş).</summary>
public enum InvoicePeriodStatus
{
    Planlandi = 0,
    Kesildi = 1,
    Atlandi = 2
}
