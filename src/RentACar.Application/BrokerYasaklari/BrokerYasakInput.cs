namespace RentACar.Application.BrokerYasaklari;

/// <summary>Broker/kaynak satış yasağı oluştur/güncelle giriş modeli. Kapsam/kısıt alanları opsiyonel.</summary>
public sealed class BrokerYasakInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public string? Kaynak { get; set; }
    public string? AracGrupKod { get; set; }
    public string? Bolge { get; set; }

    public int? MinGun { get; set; }
    public bool TumSatisKapali { get; set; }

    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    public bool Aktif { get; set; } = true;
}
