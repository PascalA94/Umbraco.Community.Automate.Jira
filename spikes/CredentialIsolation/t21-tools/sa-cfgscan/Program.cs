using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
// Security-analyst check: which methods in Automate assemblies touch configuration, by
// (1) any method signature with an IConfiguration*/IServiceProvider parameter (proper decoder),
// (2) any IL reference (call/callvirt/newobj/ldtoken/ldsfld) to a member or type in Microsoft.Extensions.Configuration,
//     to System.Configuration.ConfigurationManager, or to Environment.GetEnvironmentVariable(s).
foreach (var path in args)
{
    using var fs = File.OpenRead(path); using var pe = new PEReader(fs); var md = pe.GetMetadataReader();
    var asm = md.GetString(md.GetAssemblyDefinition().Name) + " " + md.GetAssemblyDefinition().Version;
    var prov = new P(md); int methods = 0, bodies = 0, sigFail = 0;
    var sigHits = new SortedSet<string>(); var ilHits = new SortedDictionary<string, SortedSet<string>>();
    foreach (var th in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(th); var tn = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
        foreach (var mh in t.GetMethods())
        {
            methods++; var m = md.GetMethodDefinition(mh); var mn = md.GetString(m.Name);
            try {
                var sig = m.DecodeSignature(prov, null);
                foreach (var p in sig.ParameterTypes)
                    if (p.Contains("Microsoft.Extensions.Configuration.IConfiguration") || p.Contains("System.IServiceProvider"))
                        sigHits.Add($"{tn}::{mn}({string.Join(", ", sig.ParameterTypes.Select(Short))})");
            } catch { sigFail++; }
            if (m.RelativeVirtualAddress == 0) continue;
            bodies++;
            var il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILReader();
            while (il.RemainingBytes > 0)
            {
                int op = il.ReadByte(); if (op == 0xFE) op = 0xFE00 | il.ReadByte();
                int tokenOperand = op switch { 0x28 or 0x6F or 0x73 or 0xD0 or 0x7E or 0x7F or 0x7B or 0x80 or 0x7D or 0xFE06 or 0xFE07 => 4, _ => -1 };
                if (tokenOperand == 4)
                {
                    var tok = il.ReadInt32(); var name = Resolve(md, prov, tok);
                    if (name != null && Environment.GetEnvironmentVariable("DUMP") is string dm && (tn + "::" + mn).Contains(dm)) Console.WriteLine($"    DUMP {tn}::{mn} -> {name}");
                    if (name != null && (name.Contains("Microsoft.Extensions.Configuration.") || name.StartsWith("System.Configuration.ConfigurationManager") || name.Contains("Environment::GetEnvironmentVariable") || name.Contains("OptionsBuilderConfigurationExtensions") || name.Contains("OptionsConfigurationServiceCollectionExtensions") || name.Contains("IUmbracoBuilder::get_Config") || name.Contains("ConfigurationSection")))
                    { if (!ilHits.TryGetValue(tn + "::" + mn, out var s)) ilHits[tn + "::" + mn] = s = new(); s.Add(name); }
                    continue;
                }
                Skip(ref il, op);
            }
        }
    }
    Console.WriteLine($"== {asm}: {methods} methods, {bodies} IL bodies, {sigFail} signatures not decoded");
    foreach (var s in sigHits) Console.WriteLine("  SIG " + s);
    foreach (var kv in ilHits) Console.WriteLine($"  IL  {kv.Key} -> {string.Join(", ", kv.Value)}");
}
static string Short(string s) => s.Replace("Microsoft.Extensions.", "MEX.");
static string Resolve(MetadataReader md, P prov, int tok)
{
    var h = MetadataTokens.EntityHandle(tok);
    try {
        switch (h.Kind)
        {
            case HandleKind.MemberReference: var mr = md.GetMemberReference((MemberReferenceHandle)h); return Parent(md, prov, mr.Parent) + "::" + md.GetString(mr.Name);
            case HandleKind.MethodSpecification: var ms = md.GetMethodSpecification((MethodSpecificationHandle)h); return Resolve(md, prov, MetadataTokens.GetToken(ms.Method)) + "<" + string.Join(",", ms.DecodeSignature(prov, null)) + ">";
            case HandleKind.TypeReference: case HandleKind.TypeDefinition: case HandleKind.TypeSpecification: return Parent(md, prov, h);
            default: return null;
        }
    } catch { return null; }
}
static string Parent(MetadataReader md, P prov, EntityHandle h) => h.Kind switch
{
    HandleKind.TypeReference => prov.GetTypeFromReference(md, (TypeReferenceHandle)h, 0),
    HandleKind.TypeDefinition => prov.GetTypeFromDefinition(md, (TypeDefinitionHandle)h, 0),
    HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(prov, null),
    _ => "?"
};
static void Skip(ref BlobReader il, int op)
{
    int n = op switch {
        0x20 or 0x22 or 0x29 or 0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0xDD or 0x72 or 0x74 or 0x75 or 0x79 or 0x8C or 0x8D or 0x8F or 0xA3 or 0xA4 or 0xA5 or 0xC2 or 0xC6 or 0x70 or 0x71 or 0x81 or 0x27 or 0x7C or 0xD0 or 0xFE15 or 0xFE16 or 0xFE1C or 0x6F or 0x73 or 0x28 => 4,
        0x21 or 0x23 => 8,
        0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDE or 0xFE12 or 0xFE19 => 1,
        0xFE09 or 0xFE0A or 0xFE0B or 0xFE0C or 0xFE0D or 0xFE0E => 2,
        0x45 => -1, _ => 0 };
    if (n == -1) { int c = il.ReadInt32(); il.Offset += 4 * c; return; }
    il.Offset += n;
}
sealed class P : ISignatureTypeProvider<string, object>
{
    readonly MetadataReader _m; public P(MetadataReader m) { _m = m; }
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object c, int i) => "!" + i;
    public string GetModifiedType(string mod, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var d = r.GetTypeDefinition(h); var ns = r.GetString(d.Namespace); var dt = d.GetDeclaringType(); return (dt.IsNil ? ns : GetTypeFromDefinition(r, dt, 0)) + "." + r.GetString(d.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { var d = r.GetTypeReference(h); var ns = r.GetString(d.Namespace); if (d.ResolutionScope.Kind == HandleKind.TypeReference) ns = GetTypeFromReference(r, (TypeReferenceHandle)d.ResolutionScope, 0); return ns + "." + r.GetString(d.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
