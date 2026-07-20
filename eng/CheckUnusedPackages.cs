// Detects declared <PackageReference> items that have no detectable compile-time usage.
//
// Part 1 of issue #711. Run as a .NET 10 file-based app (no project required):
//
//     dotnet run eng/CheckUnusedPackages.cs -- [repoRoot] [--fail-on-unused]
//
// How it decides "unused" (all signals come from real build artifacts, not guesses):
//   * Only DIRECT PackageReference items are considered (transitive deps are ignored).
//   * A package is auto-skipped when it contributes no compile-time assembly for the
//     project's target framework (project.assets.json "compile" group empty or "_._").
//     This automatically excludes analyzers, build/tooling packages (PrivateAssets),
//     and native runtime providers such as SQLitePCLRaw.bundle_e_sqlite3 -- none of
//     which are referenced by C# symbols and none of which should ever be flagged.
//   * For the remaining packages, the checker reads the actual public namespaces and
//     any XmlnsDefinition xml-namespace URLs out of the restored assemblies (via
//     System.Reflection.Metadata) and looks for usage in the project's C# (namespace
//     tokens) and XAML (clr-namespace assembly= and xmlns URLs). If nothing matches,
//     the package is reported.
//
// The check is NON-BLOCKING by default: it prints GitHub Actions ::warning annotations
// and exits 0. Pass --fail-on-unused to make it exit 1 (only once it has proven quiet
// for a few PRs, per the issue's suggested rollout).
//
// The allowlist below is the documented tuning knob for the rare package that is
// legitimately declared but exercised only through a channel this checker cannot see.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repoRoot = Directory.GetCurrentDirectory();
bool failOnUnused = false;
foreach (var a in args)
{
    if (a == "--fail-on-unused") failOnUnused = true;
    else if (!a.StartsWith("--", StringComparison.Ordinal)) repoRoot = Path.GetFullPath(a);
}

// Packages legitimately declared but not detectably referenced by C# symbols or XAML.
// Keep this list short and documented -- most build/native/analyzer packages are already
// auto-skipped by the "no compile assembly" rule above.
var allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // Provider package: its own assembly exposes only EFCore.BulkExtensions.SqlAdapters.Sqlite,
    // while the EFCore.BulkExtensions API namespace the code actually calls (BulkInsert, etc.) is
    // delivered by its transitive EFCore.BulkExtensions.Core dependency. The direct reference is
    // required to select the SQLite adapter, so it can never be attributed by first-party surface.
    "EFCore.BulkExtensions.Sqlite",
};

var projects = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
    .Select(p => p.Replace('\\', '/'))
    .Where(p => !p.Contains("/Daqifi.Desktop.Setup/"))
    .Where(p => !p.Contains("/obj/") && !p.Contains("/bin/"))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (projects.Count == 0)
{
    Console.Error.WriteLine($"No .csproj files found under '{repoRoot}'.");
    return 2;
}

Console.WriteLine($"Unused-PackageReference check over {projects.Count} project(s) in {repoRoot}");
int totalUnused = 0;
int skippedNoAssets = 0;

foreach (var proj in projects)
{
    var projDir = Path.GetDirectoryName(proj)!;
    var projName = Path.GetFileName(proj);
    var assetsPath = Path.Combine(projDir, "obj", "project.assets.json");
    if (!File.Exists(assetsPath))
    {
        Console.WriteLine($"  {projName}: no obj/project.assets.json (not restored) -- skipped");
        skippedNoAssets++;
        continue;
    }

    var declared = ParseDirectPackageRefs(proj);
    if (declared.Count == 0)
    {
        continue;
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
    var root = doc.RootElement;
    var folders = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToList();
    var target = root.GetProperty("targets").EnumerateObject().First().Value;
    var libraries = root.GetProperty("libraries");

    var (csText, xamlText) = LoadCorpus(projDir);

    foreach (var pkg in declared.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
    {
        if (allowlist.Contains(pkg))
        {
            continue;
        }

        // Locate the "<id>/<version>" entry for this package in the target graph.
        JsonElement entry = default;
        string? libKey = null;
        foreach (var t in target.EnumerateObject())
        {
            var slash = t.Name.IndexOf('/');
            if (slash > 0 && string.Equals(t.Name[..slash], pkg, StringComparison.OrdinalIgnoreCase))
            {
                entry = t.Value;
                libKey = t.Name;
                break;
            }
        }
        if (libKey is null)
        {
            continue; // Unresolved (e.g. a project reference dressed as a package) -- ignore.
        }

        if (!entry.TryGetProperty("compile", out var compile))
        {
            continue; // No compile-time surface -> not detectable, auto-skip.
        }
        var compileKeys = compile.EnumerateObject()
            .Select(c => c.Name)
            .Where(k => !k.EndsWith("_._", StringComparison.Ordinal))
            .ToList();
        if (compileKeys.Count == 0)
        {
            continue; // Placeholder / native / analyzer package -> auto-skip.
        }

        var libPath = libraries.GetProperty(libKey).GetProperty("path").GetString() ?? "";
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var xmlnsUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var asmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ck in compileKeys)
        {
            var dll = ResolveDll(folders, libPath, ck);
            if (dll is null)
            {
                continue;
            }
            asmNames.Add(Path.GetFileNameWithoutExtension(ck));
            CollectFromAssembly(dll, namespaces, xmlnsUrls);
        }

        if (namespaces.Count == 0 && xmlnsUrls.Count == 0)
        {
            continue; // Could not read any public surface -> avoid a false positive.
        }

        if (!IsUsed(namespaces, xmlnsUrls, asmNames, csText, xamlText))
        {
            var rel = Path.GetRelativePath(repoRoot, proj).Replace('\\', '/');
            Console.WriteLine(
                $"::warning file={rel}::[unused-package] '{pkg}' is declared in {projName} " +
                $"but no C# or XAML usage was detected. If this is intentional (used only via a " +
                $"channel the checker cannot see), add it to the allowlist in eng/CheckUnusedPackages.cs.");
            totalUnused++;
        }
    }
}

Console.WriteLine(
    $"Done. {totalUnused} likely-unused PackageReference(s) found" +
    (skippedNoAssets > 0 ? $" ({skippedNoAssets} project(s) skipped: not restored)." : "."));

if (totalUnused > 0 && failOnUnused)
{
    return 1;
}
return 0;

// ---- helpers ----

static HashSet<string> ParseDirectPackageRefs(string proj)
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var xd = XDocument.Load(proj);
    foreach (var e in xd.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
    {
        var inc = e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value;
        if (!string.IsNullOrWhiteSpace(inc))
        {
            set.Add(inc.Trim());
        }
    }
    return set;
}

static (string cs, string xaml) LoadCorpus(string projDir)
{
    var sbCs = new StringBuilder();
    var sbXaml = new StringBuilder();
    foreach (var file in Directory.EnumerateFiles(projDir, "*.*", SearchOption.AllDirectories))
    {
        var norm = file.Replace('\\', '/');
        // bin/ holds build outputs, never source. obj/ IS kept: it carries SDK-generated
        // compile inputs -- global usings (e.g. MSTest's Microsoft.VisualStudio.TestTools.UnitTesting)
        // and XAML .g.cs -- that are genuine references and must count as usage.
        if (norm.Contains("/bin/"))
        {
            continue;
        }
        if (norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            sbCs.Append(File.ReadAllText(file)).Append('\n');
        }
        else if (norm.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            sbXaml.Append(File.ReadAllText(file)).Append('\n');
        }
    }
    return (sbCs.ToString(), sbXaml.ToString());
}

static string? ResolveDll(List<string> folders, string libPath, string compileKey)
{
    var rel = compileKey.Replace('/', Path.DirectorySeparatorChar);
    foreach (var f in folders)
    {
        var p = Path.Combine(f, libPath, rel);
        if (File.Exists(p))
        {
            return p;
        }
    }
    return null;
}

static void CollectFromAssembly(string dll, HashSet<string> namespaces, HashSet<string> xmlnsUrls)
{
    try
    {
        using var fs = File.OpenRead(dll);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata)
        {
            return;
        }
        var mr = pe.GetMetadataReader();

        foreach (var th in mr.TypeDefinitions)
        {
            var td = mr.GetTypeDefinition(th);
            if ((td.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            {
                continue; // Only the public, top-level API surface carries usable namespaces.
            }
            var ns = mr.GetString(td.Namespace);
            if (!string.IsNullOrEmpty(ns))
            {
                namespaces.Add(ns);
            }
        }

        // Type-forwarding facade assemblies (e.g. MSTest.TestFramework forwards
        // Microsoft.VisualStudio.TestTools.UnitTesting) expose their API as ExportedType
        // rows rather than TypeDefinitions -- count those namespaces too.
        foreach (var eth in mr.ExportedTypes)
        {
            var et = mr.GetExportedType(eth);
            if ((et.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            {
                continue;
            }
            var ns = mr.GetString(et.Namespace);
            if (!string.IsNullOrEmpty(ns))
            {
                namespaces.Add(ns);
            }
        }

        foreach (var cah in mr.GetAssemblyDefinition().GetCustomAttributes())
        {
            var ca = mr.GetCustomAttribute(cah);
            if (AttributeTypeName(mr, ca) != "XmlnsDefinitionAttribute")
            {
                continue;
            }
            try
            {
                var br = mr.GetBlobReader(ca.Value);
                if (br.ReadUInt16() != 1)
                {
                    continue; // Not the standard custom-attribute prolog.
                }
                var url = br.ReadSerializedString(); // First ctor arg = xml namespace URL.
                if (!string.IsNullOrEmpty(url))
                {
                    xmlnsUrls.Add(url);
                }
            }
            catch
            {
                // Ignore attributes we cannot decode.
            }
        }
    }
    catch
    {
        // Unreadable assembly -> contributes nothing; caller treats as no surface.
    }
}

static string AttributeTypeName(MetadataReader mr, CustomAttribute ca)
{
    switch (ca.Constructor.Kind)
    {
        case HandleKind.MemberReference:
            var m = mr.GetMemberReference((MemberReferenceHandle)ca.Constructor);
            if (m.Parent.Kind == HandleKind.TypeReference)
            {
                return mr.GetString(mr.GetTypeReference((TypeReferenceHandle)m.Parent).Name);
            }
            break;
        case HandleKind.MethodDefinition:
            var md = mr.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
            return mr.GetString(mr.GetTypeDefinition(md.GetDeclaringType()).Name);
    }
    return "";
}

static bool IsUsed(
    HashSet<string> namespaces,
    HashSet<string> xmlnsUrls,
    HashSet<string> asmNames,
    string csText,
    string xamlText)
{
    // C#: any of the package's public namespaces appears as a dotted token.
    foreach (var ns in namespaces)
    {
        var pattern = $@"(?<![A-Za-z0-9_.]){Regex.Escape(ns)}(?![A-Za-z0-9_])";
        if (Regex.IsMatch(csText, pattern))
        {
            return true;
        }
    }

    if (xamlText.Length > 0)
    {
        // XAML: assembly-qualified clr-namespace, e.g. assembly=OxyPlot.Wpf
        foreach (var asm in asmNames)
        {
            if (Regex.IsMatch(xamlText, $@"assembly\s*=\s*{Regex.Escape(asm)}(?![A-Za-z0-9_.])"))
            {
                return true;
            }
        }
        // XAML: URL xmlns declared by the assembly's [XmlnsDefinition], e.g. MahApps/Behaviors.
        foreach (var url in xmlnsUrls)
        {
            if (xamlText.Contains(url, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    }

    return false;
}
