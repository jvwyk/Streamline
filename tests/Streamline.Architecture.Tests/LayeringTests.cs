using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Streamline.Architecture.Tests;

/// <summary>
/// Enforces the dependency direction described in docs/streamline-plan.md §5.1
/// and docs/AGENTS.md "Project Structure". Failures collect every offending
/// reference across every rule before asserting, so the failure message
/// names every violation at once rather than stopping at the first.
/// </summary>
public class LayeringTests
{
    private static readonly IReadOnlyList<string> AllStreamlineProjects = new[]
    {
        "Streamline.Core",
        "Streamline.Domain",
        "Streamline.Application",
        "Streamline.Infrastructure",
        "Streamline.Registry.Postgres",
        "Streamline.Registry.Yaml",
        "Streamline.Registry.Hybrid",
        "Streamline.Files.Delimited",
        "Streamline.Files.Xlsx",
        "Streamline.Files.Json",
        "Streamline.Files.Zip",
        "Streamline.Console",
    };

    /// <summary>
    /// The full set of Streamline.* projects a given project is allowed to
    /// reference directly (via ProjectReference). Transitive references
    /// (e.g., Hybrid pulling in Application through Registry.Postgres) are
    /// fine — this is a direct-reference check.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedDirectReferences = new()
    {
        ["Streamline.Core"] = Array.Empty<string>(),
        ["Streamline.Domain"] = new[] { "Streamline.Core" },
        ["Streamline.Application"] = new[] { "Streamline.Core", "Streamline.Domain" },
        ["Streamline.Infrastructure"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Registry.Postgres"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Registry.Yaml"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Registry.Hybrid"] = new[] { "Streamline.Domain", "Streamline.Registry.Postgres", "Streamline.Registry.Yaml" },
        ["Streamline.Files.Delimited"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Files.Xlsx"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Files.Json"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Files.Zip"] = new[] { "Streamline.Core", "Streamline.Domain", "Streamline.Application" },
        ["Streamline.Console"] = new[]
        {
            "Streamline.Core",
            "Streamline.Domain",
            "Streamline.Application",
            "Streamline.Infrastructure",
            "Streamline.Registry.Postgres",
            "Streamline.Registry.Yaml",
            "Streamline.Registry.Hybrid",
            "Streamline.Files.Delimited",
            "Streamline.Files.Xlsx",
            "Streamline.Files.Json",
            "Streamline.Files.Zip",
        },
    };

    [Fact]
    public void Layering_rules_are_satisfied()
    {
        var violations = new List<string>();

        CheckProjectReferenceGraphFromCsproj(violations);
        CheckNoSrcProjectReferencesConsole(violations);
        CheckNoSrcProjectReferencesATestAssembly(violations);
        CheckNetArchTypeRules(violations);

        violations.Should().BeEmpty(
            "the dependency direction in docs/streamline-plan.md §5.1 must be preserved; " +
            "every entry in this list names an illegal reference that needs to be removed");
    }

    /// <summary>
    /// Every type in <c>Streamline.Domain.Abstractions</c> must be a
    /// public interface. Classes (or internals) in Abstractions/ would
    /// be miscategorised — the namespace is reserved for engine-side
    /// contracts that consumers / implementations sit on top of.
    /// </summary>
    [Fact]
    public void Abstractions_namespace_contains_only_public_interfaces()
    {
        var domainAssembly = LoadStreamlineAssembly("Streamline.Domain");
        var abstractionTypes = domainAssembly
            .GetTypes()
            .Where(t => t.Namespace == "Streamline.Domain.Abstractions")
            .ToArray();

        var violations = new List<string>();

        foreach (var type in abstractionTypes)
        {
            if (!type.IsInterface)
            {
                violations.Add(
                    $"{type.FullName} is not an interface (Abstractions/ is reserved for interface contracts).");
            }
            if (!type.IsPublic)
            {
                violations.Add(
                    $"{type.FullName} is not public (interfaces in Abstractions/ are contracts for cross-assembly implementers).");
            }
        }

        violations.Should().BeEmpty(
            "Streamline.Domain.Abstractions is reserved for public interfaces only");
    }

    [Fact]
    public void Library_boundaries_are_respected()
    {
        // Specialised reader libraries live in exactly one project so we can
        // swap implementations without fanout. Pg drivers live in the two
        // Postgres-backed packages. None of these dependencies exist yet in
        // Phase 0 — the rules are codified now so the first violation in a
        // later phase fails loudly.
        var libraryHomes = new Dictionary<string, string[]>
        {
            ["CsvHelper"] = new[] { "Streamline.Files.Delimited" },
            ["ClosedXML"] = new[] { "Streamline.Files.Xlsx" },
            ["ExcelDataReader"] = new[] { "Streamline.Files.Xls" }, // added when first .xls job migrates
            ["YamlDotNet"] = new[] { "Streamline.Registry.Yaml", "Streamline.Registry.Hybrid" },
            ["Npgsql"] = new[] { "Streamline.Infrastructure", "Streamline.Registry.Postgres" },
            ["Dapper"] = new[] { "Streamline.Infrastructure", "Streamline.Registry.Postgres" },
        };

        var violations = new List<string>();

        foreach (var (library, homeProjects) in libraryHomes)
        {
            foreach (var project in AllStreamlineProjects)
            {
                if (homeProjects.Contains(project))
                {
                    continue;
                }

                var csproj = TryLoadCsproj(project);
                if (csproj is null)
                {
                    continue;
                }

                var hasReference = csproj
                    .Descendants("PackageReference")
                    .Select(p => p.Attribute("Include")?.Value)
                    .Any(name => string.Equals(name, library, StringComparison.Ordinal));

                if (hasReference)
                {
                    violations.Add(
                        $"{project} references {library}; only {string.Join(", ", homeProjects)} may");
                }
            }
        }

        violations.Should().BeEmpty(
            "specialised libraries must stay inside their wrapping project so they can be " +
            "swapped without collateral changes");
    }

    private static void CheckProjectReferenceGraphFromCsproj(List<string> violations)
    {
        foreach (var project in AllStreamlineProjects)
        {
            var csproj = TryLoadCsproj(project);
            if (csproj is null)
            {
                violations.Add($"{project}: csproj could not be located for inspection");
                continue;
            }

            var allowed = new HashSet<string>(AllowedDirectReferences[project], StringComparer.Ordinal);
            var refs = csproj
                .Descendants("ProjectReference")
                .Select(pr => ProjectNameFromPath(pr.Attribute("Include")?.Value))
                .Where(n => n is not null && n.StartsWith("Streamline.", StringComparison.Ordinal))
                .Select(n => n!);

            foreach (var reference in refs)
            {
                if (!allowed.Contains(reference))
                {
                    violations.Add($"{project} references {reference} (not allowed by layer policy)");
                }
            }
        }
    }

    private static void CheckNoSrcProjectReferencesConsole(List<string> violations)
    {
        foreach (var project in AllStreamlineProjects)
        {
            if (project == "Streamline.Console")
            {
                continue;
            }

            var csproj = TryLoadCsproj(project);
            if (csproj is null)
            {
                continue;
            }

            var referencesConsole = csproj
                .Descendants("ProjectReference")
                .Select(pr => ProjectNameFromPath(pr.Attribute("Include")?.Value))
                .Any(n => n == "Streamline.Console");

            if (referencesConsole)
            {
                violations.Add($"{project} references Streamline.Console (Console must be a leaf)");
            }
        }
    }

    private static void CheckNoSrcProjectReferencesATestAssembly(List<string> violations)
    {
        foreach (var project in AllStreamlineProjects)
        {
            var csproj = TryLoadCsproj(project);
            if (csproj is null)
            {
                continue;
            }

            var testReferences = csproj
                .Descendants("ProjectReference")
                .Select(pr => ProjectNameFromPath(pr.Attribute("Include")?.Value))
                .Where(n => n is not null && n.EndsWith(".Tests", StringComparison.Ordinal));

            foreach (var testReference in testReferences)
            {
                violations.Add($"{project} references test project {testReference}");
            }
        }
    }

    /// <summary>
    /// NetArchTest type-level checks. These pass trivially in Phase 0 because
    /// no types exist yet; they enforce discipline as code lands in later
    /// phases. Type-level checks catch cases where a class from the wrong
    /// layer is used even if the csproj ProjectReference was removed.
    /// </summary>
    private static void CheckNetArchTypeRules(List<string> violations)
    {
        var rules = new (string Description, NetArchTest.Rules.TestResult Result)[]
        {
            (
                "Streamline.Core types must not depend on Streamline.Domain",
                Types.InAssembly(LoadStreamlineAssembly("Streamline.Core"))
                    .Should()
                    .NotHaveDependencyOn("Streamline.Domain")
                    .GetResult()),
            (
                "Streamline.Core types must not depend on Streamline.Application",
                Types.InAssembly(LoadStreamlineAssembly("Streamline.Core"))
                    .Should()
                    .NotHaveDependencyOn("Streamline.Application")
                    .GetResult()),
            (
                "Streamline.Domain types must not depend on Streamline.Application",
                Types.InAssembly(LoadStreamlineAssembly("Streamline.Domain"))
                    .Should()
                    .NotHaveDependencyOn("Streamline.Application")
                    .GetResult()),
            (
                "Streamline.Domain types must not depend on Streamline.Infrastructure",
                Types.InAssembly(LoadStreamlineAssembly("Streamline.Domain"))
                    .Should()
                    .NotHaveDependencyOn("Streamline.Infrastructure")
                    .GetResult()),
            (
                "Streamline.Application types must not depend on Streamline.Infrastructure",
                Types.InAssembly(LoadStreamlineAssembly("Streamline.Application"))
                    .Should()
                    .NotHaveDependencyOn("Streamline.Infrastructure")
                    .GetResult()),
        };

        foreach (var (description, result) in rules)
        {
            if (result.IsSuccessful)
            {
                continue;
            }

            var failing = result.FailingTypeNames?.ToArray() ?? Array.Empty<string>();
            var typesPart = failing.Length > 0 ? string.Join(", ", failing) : "(unknown types)";
            violations.Add($"{description}: failing types = {typesPart}");
        }
    }

    private static string? ProjectNameFromPath(string? includeValue)
    {
        if (string.IsNullOrWhiteSpace(includeValue))
        {
            return null;
        }

        // csproj paths use Windows-style '\' separators regardless of host OS.
        // Normalise before handing to Path.GetFileNameWithoutExtension, which
        // only recognises the host separator.
        var normalised = includeValue.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalised);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static XDocument? TryLoadCsproj(string project)
    {
        var path = ResolveCsprojPath(project);
        return path is not null && File.Exists(path) ? XDocument.Load(path) : null;
    }

    private static string? ResolveCsprojPath(string project)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        // src projects first, then tests/ in case a rule ever needs to inspect
        // a test csproj (no current rule does, but the helper is general).
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", project, $"{project}.csproj"),
            Path.Combine(repoRoot, "tests", project, $"{project}.csproj"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Walks up from the test assembly's location until we find a directory
    /// containing Streamline.slnx (repo root marker). This lets the test run
    /// from any build output location without a hardcoded path.
    /// </summary>
    private static string? FindRepoRoot()
    {
        var start = Path.GetDirectoryName(typeof(LayeringTests).Assembly.Location);
        var current = start is null ? null : new DirectoryInfo(start);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Streamline.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "Streamline.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Assembly LoadStreamlineAssembly(string name)
    {
        return AppDomain.CurrentDomain.Load(name);
    }
}
