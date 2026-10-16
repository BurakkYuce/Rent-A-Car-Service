using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.XPath;
using Wmhelp.XPath2;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Üretilen UBL-TR belgesini GİB'in YAYINLADIĞI kurallara karşı doğrular — üretim kodunu ÇAĞIRMAYAN bağımsız
/// oracle. Kaynak dosyalar <c>Fixtures/UblTr121/</c> altındadır (kaynak ve lisans: <c>KAYNAK-VE-LISANS.txt</c>).
/// <list type="number">
/// <item><b>XSD</b> — UBL-TR 1.2.1 paketindeki <c>xsdrt/maindoc/UBL-Invoice-2.1.xsd</c> (GİB'in daralttığı UBL 2.1).</item>
/// <item><b>Şematron</b> — <c>UBL-TR_Main_Schematron.xml</c> + <c>Common</c> + <c>Codelist</c>. GİB şematronu XPath 2.0
/// kullanır (<c>matches</c>, <c>xs:date</c>, <c>current-date</c>); .NET'in XSLT 1.0 motoru onu çalıştıramadığı için
/// burada küçük bir ISO-şematron yorumlayıcısı var: <c>include</c> çözülür, soyut kurallar <c>extends</c> ile
/// açılır, <c>let</c>'ler genel değişken olur, her desen içinde bir düğüm yalnız İLK eşleşen kurala girer, assert'ler
/// XPath2.Net ile değerlendirilir. <c>$type</c> GİB'in parametresidir: <c>efatura</c> ya da e-Arşiv için <c>earchive</c>.</item>
/// </list>
/// </summary>
public static class UblTrValidator
{
    private const string Sch = "http://purl.oclc.org/dsdl/schematron";

    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);
    private static readonly Lazy<CompiledSchematron> Schematron = new(LoadSchematron);

    public static string FixtureRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "Fixtures", "UblTr121"))) d = d.Parent;
            return d is null
                ? throw new DirectoryNotFoundException("Fixtures/UblTr121 bulunamadı.")
                : Path.Combine(d.FullName, "Fixtures", "UblTr121");
        }
    }

    // ------------------------------------------------------------------ XSD

    /// <summary>XSD hataları (boş liste = geçerli).</summary>
    public static IReadOnlyList<string> ValidateXsd(byte[] xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Schemas.Value };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message} (satır {e.Exception?.LineNumber})");
        using var reader = XmlReader.Create(new MemoryStream(xml), settings);
        while (reader.Read()) { }
        return errors;
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        // UBL-xmldsig-core-schema DTD bildirimi taşır; DTD işlenir ama dış kaynak çözülmez (yerel dosya dışı yok).
        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        var path = Path.Combine(FixtureRoot, "xsdrt", "maindoc", "UBL-Invoice-2.1.xsd");
        using (var reader = XmlReader.Create(path, readerSettings)) set.Add(null, reader);
        set.Compile();
        return set;
    }

    // ------------------------------------------------------------------ şematron

    /// <summary>Şematron ihlalleri (boş liste = geçerli). <paramref name="type"/>: <c>efatura</c> | <c>earchive</c>.</summary>
    public static IReadOnlyList<string> ValidateSchematron(byte[] xml, string type)
    {
        var compiled = Schematron.Value;
        var doc = XDocument.Load(new MemoryStream(xml));
        var root = doc.CreateNavigator();

        var vars = new Dictionary<XmlQualifiedName, object>();
        foreach (var (name, expr) in compiled.Lets)
        {
            vars[new XmlQualifiedName(name)] = name == "type"
                ? type
                : Materialize(expr.Evaluate(new NodeProvider(root), Snapshot(vars)));
        }

        var failures = new List<string>();
        foreach (var pattern in compiled.Patterns)
        {
            var fired = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var rule in pattern)
            {
                var nodes = root.XPath2SelectNodes(rule.Context, compiled.Ns);
                while (nodes.MoveNext())
                {
                    var node = nodes.Current!.Clone();
                    if (node.UnderlyingObject is not { } underlying || !fired.Add(underlying)) continue;
                    foreach (var assert in rule.Asserts)
                    {
                        var ok = assert.Test.Evaluate(new NodeProvider(node), Snapshot(vars)) is true;
                        if (!ok) failures.Add($"[{assert.RuleId}] {Message(assert, node, compiled, vars)}");
                    }
                }
            }
        }
        return failures;
    }

    private static Dictionary<XmlQualifiedName, object> Snapshot(Dictionary<XmlQualifiedName, object> vars)
        => vars.ToDictionary(kv => kv.Key, kv => kv.Value is XPath2NodeIterator it ? it.Clone() : kv.Value);

    private static object Materialize(object value) => value switch
    {
        XPath2NodeIterator it => it.Clone(),
        null => string.Empty,
        _ => value,
    };

    private static string Message(SchAssert assert, XPathNavigator node, CompiledSchematron compiled,
        Dictionary<XmlQualifiedName, object> vars)
    {
        var sb = new StringBuilder();
        foreach (var part in assert.Message.Nodes())
        {
            switch (part)
            {
                case XText t: sb.Append(t.Value); break;
                case XElement e when e.Name == XName.Get("name", Sch): sb.Append(node.Name); break;
                case XElement e when e.Name == XName.Get("value-of", Sch):
                    try
                    {
                        var expr = XPath2Expression.Compile("string((" + (string?)e.Attribute("select") + ")[1])", compiled.Ns);
                        sb.Append(expr.Evaluate(new NodeProvider(node), Snapshot(vars)));
                    }
                    catch (Exception ex) { sb.Append("{" + ex.GetType().Name + "}"); }
                    break;
            }
        }
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record SchAssert(string RuleId, XPath2Expression Test, XElement Message);
    private sealed record SchRule(string Context, IReadOnlyList<SchAssert> Asserts);

    private sealed record CompiledSchematron(
        XmlNamespaceManager Ns, IReadOnlyList<(string Name, XPath2Expression Expr)> Lets, IReadOnlyList<IReadOnlyList<SchRule>> Patterns);

    private static CompiledSchematron LoadSchematron()
    {
        var dir = Path.Combine(FixtureRoot, "schematron");
        var main = XDocument.Load(Path.Combine(dir, "UBL-TR_Main_Schematron.xml"));
        XName S(string local) => XName.Get(local, Sch);

        // 1) include'ları çöz: href="dosya#id" → o dosyadaki id'li eleman (desen) yerine konur.
        foreach (var include in main.Root!.Elements(S("include")).ToList())
        {
            var href = (string)include.Attribute("href")!;
            var (file, id) = (href.Split('#')[0], href.Split('#')[1]);
            var included = XDocument.Load(Path.Combine(dir, file)).Descendants()
                .Single(e => (string?)e.Attribute("id") == id);
            include.ReplaceWith(included);
        }

        var ns = new XmlNamespaceManager(new NameTable());
        foreach (var n in main.Root.Elements(S("ns")))
            ns.AddNamespace((string)n.Attribute("prefix")!, (string)n.Attribute("uri")!);
        ns.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");

        // 2) let'ler — şema ve kod-listesi deseni düzeyinde (belge sırasıyla; sonraki önceki­yi kullanabilir).
        var lets = main.Root.Descendants()
            .Where(e => e.Name.LocalName == "let" && e.Parent?.Name.LocalName is "schema" or "pattern")
            .Select(e => ((string)e.Attribute("name")!, XPath2Expression.Compile((string)e.Attribute("value")!, ns)))
            .ToList();

        // 3) soyut kurallar
        var abstracts = main.Root.Descendants(S("rule"))
            .Where(r => (string?)r.Attribute("abstract") == "true")
            .ToDictionary(r => (string)r.Attribute("id")!);

        List<SchAssert> AssertsOf(XElement rule, string ruleId)
        {
            var list = new List<SchAssert>();
            foreach (var child in rule.Elements())
            {
                if (child.Name == S("assert"))
                    list.Add(new SchAssert(ruleId, XPath2Expression.Compile("boolean(" + (string)child.Attribute("test")! + ")", ns), child));
                else if (child.Name == S("extends"))
                {
                    var id = (string)child.Attribute("rule")!;
                    list.AddRange(AssertsOf(abstracts[id], id));
                }
            }
            return list;
        }

        SchRule Compile(XElement rule)
        {
            var context = (string)rule.Attribute("context")!;
            var select = context.StartsWith('/') ? context : "//" + context;
            return new SchRule(select, AssertsOf(rule, (string?)rule.Attribute("id") ?? context));
        }

        // 4) desenler (soyut kural/kod listesi desenleri hariç) + desen DIŞINDAKİ genel kurallar ayrı bir desen sayılır.
        var patterns = main.Root.Elements(S("pattern"))
            .Where(p => p.Elements(S("rule")).Any(r => (string?)r.Attribute("abstract") != "true"))
            .Select(p => (IReadOnlyList<SchRule>)p.Elements(S("rule"))
                .Where(r => (string?)r.Attribute("abstract") != "true").Select(Compile).ToList())
            .ToList();
        var general = main.Root.Elements(S("rule")).Select(Compile).ToList();
        if (general.Count > 0) patterns.Add(general);

        return new CompiledSchematron(ns, lets, patterns);
    }
}
