using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RentACar.Application.Integrations;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Entegratör TEST ÇİFTİ (gerçek bir entegratörün API'si taklit EDİLMEZ — yalnız port sözleşmesi). Bir GİB
/// entegratörünün iki temel davranışını modeller: (1) aynı ETTN'i ikinci kez kabul etmez, (2) daha önce kabul ettiği
/// ETTN'i durum sorgusunda tanır. Her çağrı sayılır; testler "kaç kez gönderildi" sorusunu buradan cevaplar.
/// </summary>
public sealed class FakeEInvoiceIntegrator : IEInvoiceIntegrator
{
    public const string TestKey = "test-entegrator";

    public string Key => TestKey;
    public string Ad => "Test entegratörü (sahte)";

    /// <summary>Kabul edilen belgeler (ETTN → gönderim).</summary>
    public ConcurrentDictionary<Guid, EInvoiceSubmission> Accepted { get; } = new();
    /// <summary>Her SendAsync çağrısı (kabul edilmese de).</summary>
    public ConcurrentQueue<EInvoiceSubmission> SendCalls { get; } = new();
    public ConcurrentQueue<Guid> StatusCalls { get; } = new();
    public ConcurrentQueue<string> RecipientCalls { get; } = new();
    public ConcurrentQueue<(Guid Ettn, string Reason)> Cancelled { get; } = new();

    /// <summary>e-Fatura kullanıcısı sayılacak VKN/TCKN'ler (geri kalanı e-Arşiv).</summary>
    public HashSet<string> RegisteredRecipients { get; } = [];
    /// <summary>Gönderim davranışını değiştirir; null dönerse varsayılan (kabul).</summary>
    public Func<EInvoiceSubmission, EInvoiceSendResult?>? OnSend { get; set; }
    /// <summary>true: belge KABUL EDİLİR ama yanıt kaybolur (istisna) — çöküş/zaman aşımı benzetimi.</summary>
    public bool AcceptThenLoseResponse { get; set; }
    /// <summary>Alıcı sorgusu geçici hata versin (mesaj olarak verilen metni döndürür).</summary>
    public string? RecipientError { get; set; }
    public TimeSpan SendDelay { get; set; }
    public EInvoiceCredentials? LastCredentials { get; private set; }

    public async Task<EInvoiceSendResult> SendAsync(EInvoiceCredentials credentials, EInvoiceSubmission document, CancellationToken ct = default)
    {
        LastCredentials = credentials;
        SendCalls.Enqueue(document);
        if (SendDelay > TimeSpan.Zero) await Task.Delay(SendDelay, ct);
        if (OnSend?.Invoke(document) is { } custom)
        {
            if (custom.Sonuc == EInvoiceOutcome.Basarili) Accepted.TryAdd(document.Ettn, document);
            return custom;
        }
        if (!Accepted.TryAdd(document.Ettn, document))
            return new EInvoiceSendResult(EInvoiceOutcome.KaliciHata, null, null, "Bu ETTN ile belge zaten var.");
        if (AcceptThenLoseResponse)
        {
            AcceptThenLoseResponse = false;
            throw new HttpRequestException("Yanıt alınamadı (zaman aşımı).");
        }
        return new EInvoiceSendResult(EInvoiceOutcome.Basarili, "REF-" + document.Ettn.ToString("N")[..8], "Kuyrukta", null);
    }

    public Task<EInvoiceStatusResult> QueryStatusAsync(EInvoiceCredentials credentials, Guid ettn, EDocumentKind kind, CancellationToken ct = default)
    {
        StatusCalls.Enqueue(ettn);
        return Task.FromResult(Accepted.ContainsKey(ettn)
            ? new EInvoiceStatusResult(true, true, "Kabul", "REF-" + ettn.ToString("N")[..8], null)
            : new EInvoiceStatusResult(true, false, null, null, null));
    }

    public Task<EInvoiceCancelResult> CancelAsync(EInvoiceCredentials credentials, Guid ettn, EDocumentKind kind, string reason, CancellationToken ct = default)
    {
        if (!Accepted.ContainsKey(ettn)) return Task.FromResult(new EInvoiceCancelResult(false, "Belge bulunamadı."));
        Cancelled.Enqueue((ettn, reason));
        return Task.FromResult(new EInvoiceCancelResult(true, null));
    }

    public Task<EInvoiceRecipientResult> QueryRecipientAsync(EInvoiceCredentials credentials, string vknTckn, CancellationToken ct = default)
    {
        RecipientCalls.Enqueue(vknTckn);
        if (RecipientError is { } error) return Task.FromResult(new EInvoiceRecipientResult(false, false, [], error));
        return Task.FromResult(RegisteredRecipients.Contains(vknTckn)
            ? new EInvoiceRecipientResult(true, true, ["urn:mail:defaultpk@alici.example"], null)
            : new EInvoiceRecipientResult(true, false, [], null));
    }

    public Task<IReadOnlyList<EInvoiceInboxItem>> FetchInboxAsync(
        EInvoiceCredentials credentials, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EInvoiceInboxItem>>([]);
}

/// <summary>Log satırlarını (mesaj + istisna metni) toplayan logger — "kişisel veri loga düştü mü?" testleri için.</summary>
public sealed class CapturingLogger : ILogger
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");

    public string All => string.Join('\n', Lines);
}
