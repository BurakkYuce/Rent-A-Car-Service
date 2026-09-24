using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Cari;

public static partial class CustomerApi
{
    /// <summary>Cari kartı. Sürüm alanlardan ÖNCE okunur (arada yazım olursa PUT 409 alır, sessizce ezmez).</summary>
    private static async Task<Results<Ok<CustomerCardDto>, ProblemHttpResult>> Card(Guid id, CustomerService customers, CancellationToken ct)
        => await CardAsync(id, customers, ct) is { } c ? TypedResults.Ok(c) : NotFound();

    private static async Task<CustomerCardDto?> CardAsync(Guid id, CustomerService customers, CancellationToken ct)
    {
        var version = await customers.GetVersionAsync(id, ct);
        var c = await customers.GetAsync(id, ct);
        return c is null ? null : CustomerInputMapper.ToCard(c, version);
    }

    /// <summary>TC/vergi no çakışması (<see cref="DuplicateCariException"/>, alt tip) alanına işaretlenir; değer mesajda YOK.</summary>
    private static async Task<T> DuplicateFieldAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (DuplicateCariException ex)
        {
            throw new ValidationException(ex.Message, ex.Field == "TC Kimlik No" ? "tcKimlik" : ex.Field == "Vergi No" ? "vergiNo" : null);
        }
    }

    private static async Task<Results<Created<CustomerCardDto>, ProblemHttpResult>> Create(
        CustomerRequest request, CustomerService customers, CancellationToken ct)
    {
        CustomerInputMapper.Limit(request);
        var input = CustomerInputMapper.ToInput(request, stored: null);
        var id = await DuplicateFieldAsync(() => customers.CreateAsync(input, ct));
        return await CardAsync(id, customers, ct) is { } card ? TypedResults.Created($"{Root}/{id}", card) : NotFound();
    }

    /// <summary>
    /// Tam değiştirme. Sıra: varlık (404) → surum zorunlu → sınırlar → gizli alan birleşimi → kilit altında sürüm (409).
    /// Gizli numaranın şifresi çözülemiyorsa (anahtar halkası uyumsuz) ve istek yeni değer vermiyorsa 400: aksi hâlde
    /// PUT "korunan" değeri boş sanıp cipher'ı silerdi.
    /// </summary>
    private static async Task<Results<Ok<CustomerCardDto>, ProblemHttpResult>> Update(
        Guid id, CustomerUpdateRequest request, CustomerService customers, CancellationToken ct)
    {
        var stored = await customers.GetAsync(id, ct);
        if (stored is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        CustomerInputMapper.Limit(request);
        RequireReadableSecrets(stored, request);
        CustomerInputMapper.RequireTaxNumberOnTypeChange(stored, request);
        var input = CustomerInputMapper.ToInput(request, stored);
        if (!await DuplicateFieldAsync(() => customers.UpdateAsync(id, input, request.Surum, ct))) return NotFound();
        return await CardAsync(id, customers, ct) is { } card ? TypedResults.Ok(card) : NotFound();
    }

    private static void RequireReadableSecrets(Customer stored, CustomerRequest request)
    {
        static bool Lost(string? cipher, string? plain, string? requested)
            => requested is null && !string.IsNullOrEmpty(cipher) && string.IsNullOrEmpty(plain);
        if (Lost(stored.TcKimlikEnc, stored.TcKimlik, request.TcKimlik))
            throw new ValidationException("Kayıtlı TC Kimlik No okunamadı; yeniden girin ya da temizleyin.", "tcKimlik");
        if (Lost(stored.EhliyetNoEnc, stored.EhliyetNo, request.EhliyetNo))
            throw new ValidationException("Kayıtlı ehliyet no okunamadı; yeniden girin ya da temizleyin.", "ehliyetNo");
        if (Lost(stored.PasaportNoEnc, stored.PasaportNo, request.PasaportNo))
            throw new ValidationException("Kayıtlı pasaport no okunamadı; yeniden girin ya da temizleyin.", "pasaportNo");
    }
}
