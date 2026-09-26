namespace RentACar.Domain.Enums;

/// <summary>FAZ-43 — şikayetin hangi sürece ait olduğu (canlı sikayet_listesi.aspx "şikayet yeri").</summary>
public enum ComplaintLocation
{
    /// <summary>Kira/teslim-dönüş sürecine ait.</summary>
    Kira = 0,
    /// <summary>Rezervasyon sürecine ait (araç henüz teslim edilmemiş olabilir).</summary>
    Rezervasyon = 1
}
