namespace RentACar.Api;

/// <summary>
/// WebApplicationFactory&lt;T&gt; entegrasyon testleri için derleme işaretçisi. Top-level
/// <c>Program</c> hem Api hem Web'de bulunduğundan ad çakışır; testler bunun yerine bu
/// benzersiz, namespace'li tipi kullanır (Api assembly'sinin giriş noktasını işaret eder).
/// </summary>
public sealed class ApiAssemblyMarker;
