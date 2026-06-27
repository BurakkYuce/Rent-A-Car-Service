namespace RentACar.Domain.Enums;

/// <summary>Servis kaydı durum akışı: Açık → Serviste → Tamamlandi (veya Iptal).</summary>
public enum ServisDurum
{
    Acik = 0,
    Serviste = 1,
    Tamamlandi = 2,
    Iptal = 3
}
