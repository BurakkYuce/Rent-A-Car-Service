using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Domain.Validation;

namespace RentACar.Application.Customers;

/// <summary>
/// Cari iş mantığı: türe göre doğrulama + benzersizlik + CRUD. Tenant izolasyonu ve
/// audit alt katmanda otomatik. Bireysel: Ad zorunlu, TC (varsa) checksum + tenant'ta
/// benzersiz. Kurumsal/Servis: Ünvan zorunlu, Vergi No (varsa) format + benzersiz.
/// </summary>
public sealed class CustomerService(ICustomerRepository repository)
{
    private readonly ICustomerRepository _repository = repository;

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<Customer?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(CustomerInput input, CancellationToken ct = default)
    {
        var n = Normalize(input);
        Validate(n);
        await EnsureUniqueAsync(n, excludeId: null, ct);

        var customer = new Customer();
        Apply(customer, n);
        await _repository.CreateAsync(customer, ct);
        return customer.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, CustomerInput input, CancellationToken ct = default)
    {
        var n = Normalize(input);
        Validate(n);
        await EnsureUniqueAsync(n, excludeId: id, ct);

        return await _repository.UpdateAsync(id, c =>
        {
            Apply(c, n);
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => _repository.DeleteAsync(id, ct);

    // ---- iç yardımcılar ----

    private static void Validate(CustomerInput n)
    {
        if (n.Tip == CariType.Bireysel)
        {
            if (string.IsNullOrWhiteSpace(n.Ad))
                throw new ValidationException("Bireysel cari için Ad zorunludur.");
            if (!string.IsNullOrEmpty(n.TcKimlik) && !TurkishIdentity.IsValidTcKimlik(n.TcKimlik))
                throw new ValidationException("TC Kimlik No geçersiz.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(n.Unvan))
                throw new ValidationException("Kurumsal/Servis cari için Ünvan zorunludur.");
            if (!string.IsNullOrEmpty(n.VergiNo) && !TurkishIdentity.IsValidVergiNoFormat(n.VergiNo))
                throw new ValidationException("Vergi No 10 haneli olmalıdır.");
        }

        if (!string.IsNullOrEmpty(n.Email) && !IsValidEmail(n.Email))
            throw new ValidationException("E-posta adresi geçersiz.");
        if (n.VadeGun < 0)
            throw new ValidationException("Vade günü negatif olamaz.");
        if (n.RiskLimiti < 0)
            throw new ValidationException("Risk limiti negatif olamaz.");
    }

    private async Task EnsureUniqueAsync(CustomerInput n, Guid? excludeId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(n.TcKimlik) && await _repository.TcKimlikExistsAsync(n.TcKimlik!, excludeId, ct))
            throw new DuplicateCariException("TC Kimlik No", n.TcKimlik!);
        if (!string.IsNullOrEmpty(n.VergiNo) && await _repository.VergiNoExistsAsync(n.VergiNo!, excludeId, ct))
            throw new DuplicateCariException("Vergi No", n.VergiNo!);
    }

    private static CustomerInput Normalize(CustomerInput input) => new()
    {
        Tip = input.Tip,
        Ad = Trim(input.Ad),
        Soyad = Trim(input.Soyad),
        TcKimlik = OnlyDigitsOrNull(input.TcKimlik),
        Unvan = Trim(input.Unvan),
        VergiDairesi = Trim(input.VergiDairesi),
        VergiNo = OnlyDigitsOrNull(input.VergiNo),
        CepTel = Trim(input.CepTel),
        Email = Trim(input.Email),
        Il = Trim(input.Il),
        Ilce = Trim(input.Ilce),
        Adres = Trim(input.Adres),
        Tarife = Trim(input.Tarife),
        VadeGun = input.VadeGun,
        RiskLimiti = input.RiskLimiti,
        KaraListe = input.KaraListe,
        Pasif = input.Pasif
    };

    private static void Apply(Customer c, CustomerInput n)
    {
        c.Tip = n.Tip;
        c.Ad = n.Ad;
        c.Soyad = n.Soyad;
        c.TcKimlik = n.TcKimlik;
        c.Unvan = n.Unvan;
        c.VergiDairesi = n.VergiDairesi;
        c.VergiNo = n.VergiNo;
        c.CepTel = n.CepTel;
        c.Email = n.Email;
        c.Il = n.Il;
        c.Ilce = n.Ilce;
        c.Adres = n.Adres;
        c.Tarife = n.Tarife;
        c.VadeGun = n.VadeGun;
        c.RiskLimiti = n.RiskLimiti;
        c.KaraListe = n.KaraListe;
        c.Pasif = n.Pasif;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? OnlyDigitsOrNull(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('.', at) > at;
    }
}
