using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
foreach (var path in args)
{
    using var fs = File.OpenRead(path);
    using var pe = new PEReader(fs);
    var md = pe.GetMetadataReader();
    var asm = md.GetString(md.GetAssemblyDefinition().Name) + " " + md.GetAssemblyDefinition().Version;
    int ctors = 0, n = 0;
    foreach (var th in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(th);
        var tname = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if (md.GetString(m.Name) != ".ctor") continue;
            ctors++;
            var sig = md.GetBlobReader(m.Signature);
            // crude: decode param types by scanning for TypeRef names of IConfiguration in the signature blob bytes
            var bytes = md.GetBlobBytes(m.Signature);
            var br = md.GetBlobReader(m.Signature);
            br.ReadByte(); int count = br.ReadCompressedInteger(); 
            var names = new List<string>();
            try {
              br.ReadByte(); // return type void
              for (int i = 0; i < count; i++) names.Add(ReadType(md, ref br));
            } catch { names.Add("?"); }
            if (names.Any(x => x.EndsWith("Configuration.IConfiguration") || x.EndsWith("IConfigurationRoot") || x.EndsWith("IConfigurationSection")))
                Console.WriteLine($"  CTOR {asm} :: {tname}({string.Join(", ", names)})");
        }
        n++;
    }
    Console.WriteLine($"{asm}: {n} types, {ctors} ctors scanned");
}
static string ReadType(MetadataReader md, ref BlobReader br)
{
    var code = (SignatureTypeCode)br.ReadByte();
    switch (code)
    {
        case SignatureTypeCode.TypeHandle:
            var h = br.ReadTypeHandle(); 
            if (h.Kind == HandleKind.TypeReference) { var r = md.GetTypeReference((TypeReferenceHandle)h); return md.GetString(r.Namespace) + "." + md.GetString(r.Name); }
            var d = md.GetTypeDefinition((TypeDefinitionHandle)h); return md.GetString(d.Namespace) + "." + md.GetString(d.Name);
        case (SignatureTypeCode)0x12: case (SignatureTypeCode)0x11: // class / valuetype
            var hh = br.ReadTypeHandle();
            if (hh.Kind == HandleKind.TypeReference) { var r = md.GetTypeReference((TypeReferenceHandle)hh); return md.GetString(r.Namespace) + "." + md.GetString(r.Name); }
            if (hh.Kind == HandleKind.TypeDefinition) { var d2 = md.GetTypeDefinition((TypeDefinitionHandle)hh); return md.GetString(d2.Namespace) + "." + md.GetString(d2.Name); }
            return "spec";
        case SignatureTypeCode.GenericTypeInstance:
            var b = br.ReadByte(); var inner = ReadType2(md, ref br); int ac = br.ReadCompressedInteger();
            for (int i = 0; i < ac; i++) ReadType(md, ref br);
            return inner + "<..>";
        case SignatureTypeCode.SZArray: return ReadType(md, ref br) + "[]";
        case SignatureTypeCode.ByReference: return ReadType(md, ref br) + "&";
        default: return code.ToString();
    }
}
static string ReadType2(MetadataReader md, ref BlobReader br)
{
    var hh = br.ReadTypeHandle();
    if (hh.Kind == HandleKind.TypeReference) { var r = md.GetTypeReference((TypeReferenceHandle)hh); return md.GetString(r.Namespace) + "." + md.GetString(r.Name); }
    var d = md.GetTypeDefinition((TypeDefinitionHandle)hh); return md.GetString(d.Namespace) + "." + md.GetString(d.Name);
}
