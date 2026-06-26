using RentACar.Application.Common;

namespace RentACar.Application.Bookings;

/// <summary>
/// Araç istenen tarih aralığında müsait değil (aktif kira çakışması). DB exclusion
/// constraint (23P01) ya da uygulama ön-kontrolü → bu. ValidationException'dan türer.
/// </summary>
public sealed class AvailabilityConflictException : ValidationException
{
    public AvailabilityConflictException(string message = "Araç bu tarih aralığında müsait değil (çakışan aktif kira).")
        : base(message)
    {
    }
}
