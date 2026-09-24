using System.Collections.Concurrent;
using RentACar.Application.TenantSettings;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// F11.1b güvenlik M6 — testte gerçek DNS'e çıkmayan TXT çözücü. Test, alan adının TXT kaydını elle "yayınlar";
/// yayınlanmamış ad boş liste döner (gerçek çözücünün "bulunamadı" davranışı).
/// </summary>
public sealed class FakeDnsTxtResolver : IDnsTxtResolver
{
    public static readonly FakeDnsTxtResolver Instance = new();

    private readonly ConcurrentDictionary<string, string[]> _records = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(string name, params string[] values) => _records[name] = values;

    public Task<IReadOnlyList<string>> ResolveTxtAsync(string name, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_records.TryGetValue(name, out var v) ? v : []);
}
