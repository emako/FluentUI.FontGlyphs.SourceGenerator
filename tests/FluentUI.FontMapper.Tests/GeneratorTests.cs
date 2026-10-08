using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FluentUI.FontMapper.Tests;

public class GeneratorTests
{
    [Fact]
    public void Assembly_attribute_generates_string_constants()
    {
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("fontawesome.ttf", "SymbolRegular", "Demo.Icons")]
            """;

        var compilation = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        var generated = Assert.Single(compilation.Generated, text => text.Contains("class SymbolRegular"));
        Assert.Contains("namespace Demo.Icons;", generated);
        Assert.Contains("public const string Heart = \"\\uF004\";", generated);
        Assert.Contains("public const string Search = \"\\uF002\";", generated);
        Assert.Contains("public const string StarEmpty = \"\\uF006\";", generated);
        Assert.DoesNotContain("enum ", generated);

        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "Demo.Icons.SymbolRegular", "Heart"));
        Assert.Equal("\uF006", GetConstant(compilation.Assembly, "Demo.Icons.SymbolRegular", "StarEmpty"));
    }

    [Fact]
    public void Partial_class_attribute_generates_into_the_declared_type()
    {
        var source = """
            using FluentUI.FontMapper;

            namespace App.Controls;

            [FontMapper("Assets/fontawesome.ttf")]
            public static partial class Icons
            {
                public const string Manual = "manual";
            }
            """;

        var compilation = Run(source, TestFonts.FontAwesome, "Assets/fontawesome.ttf");

        var generated = Assert.Single(compilation.Generated, text => text.Contains("class Icons"));
        Assert.Contains("namespace App.Controls;", generated);
        Assert.Contains("public static partial class Icons", generated);
        Assert.Contains("public const string Heart = \"\\uF004\";", generated);
        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "App.Controls.Icons", "Heart"));
        Assert.Equal("manual", GetConstant(compilation.Assembly, "App.Controls.Icons", "Manual"));
    }

    [Fact]
    public void Both_usages_can_exist_in_one_compilation()
    {
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("fontawesome.ttf", "SymbolFilled", "Generated")]

            namespace App;

            [FontMapper("fontawesome.ttf")]
            internal partial class LocalIcons
            {
            }
            """;

        var compilation = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "Generated.SymbolFilled", "Heart"));
        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "App.LocalIcons", "Heart"));
        Assert.Contains("internal partial class LocalIcons", Assert.Single(compilation.Generated, text => text.Contains("class LocalIcons")));
    }

    [Fact]
    public void Missing_font_reports_FM0001()
    {
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("missing.ttf", "Icons", "Demo")]
            """;

        var result = Run(source, TestFonts.FontAwesome, "other.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0001");
        Assert.DoesNotContain(result.Assembly.GetTypes(), type => type.Name == "Icons");
    }

    [Fact]
    public void Non_partial_class_reports_FM0003()
    {
        var source = """
            using FluentUI.FontMapper;

            [FontMapper("fontawesome.ttf")]
            public static class Icons
            {
            }
            """;

        var result = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0003");
    }

    [Fact]
    public void Wrong_constructor_reports_FM0007()
    {
        var source = """
            using FluentUI.FontMapper;

            [FontMapper("fontawesome.ttf", "Name", "Namespace")]
            public static partial class Icons
            {
            }
            """;

        var result = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0007");
    }

    [Fact]
    public void Unnamed_font_reports_FM0002()
    {
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("Segoe Fluent Icons.ttf", "Icons", "Demo")]
            """;

        var result = Run(source, TestFonts.SegoeFluentIcons, "Segoe Fluent Icons.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0002");
        Assert.DoesNotContain(result.Assembly.GetTypes(), type => type.Name == "Icons");
    }

    [Fact]
    public void Resolves_font_from_project_directory_without_relying_on_file_name_only()
    {
        var directory = Path.Combine(Path.GetTempPath(), "font-mapper-" + Guid.NewGuid().ToString("N"));
        var relative = Path.Combine("Assets", "fontawesome.ttf");
        var full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(TestFonts.FontAwesome, full);
        try
        {
            var source = """
                using FluentUI.FontMapper;

                [assembly: FontMapper("Assets/fontawesome.ttf", "Icons", "Demo")]
                """;
            var result = Run(source, fontPath: null, additionalRelativePath: null, projectDirectory: directory);
            Assert.Equal("\uF004", GetConstant(result.Assembly, "Demo.Icons", "Heart"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? GetConstant(Assembly assembly, string typeName, string fieldName)
    {
        var type = assembly.GetType(typeName);
        Assert.NotNull(type);
        var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        return field.GetRawConstantValue() as string;
    }

    private static GenerationResult Run(string source, string? fontPath, string? additionalRelativePath, string? projectDirectory = null)
    {
        string? fontDirectory = null;
        string? copiedFont = null;
        if (fontPath != null)
        {
            fontDirectory = Path.Combine(Path.GetTempPath(), "font-mapper-" + Guid.NewGuid().ToString("N"));
            copiedFont = Path.Combine(fontDirectory, additionalRelativePath!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(copiedFont)!);
            File.Copy(fontPath, copiedFont);
        }

        try
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
            var syntax = CSharpSyntaxTree.ParseText(source, parseOptions);
            var compilation = CSharpCompilation.Create(
                "FontMapper.Fixture",
                new[] { syntax },
                MetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additional = copiedFont == null
                ? ImmutableArray<AdditionalText>.Empty
                : ImmutableArray.Create<AdditionalText>(new FileAdditionalText(copiedFont));
            var driver = CSharpGeneratorDriver.Create(
                new[] { new FontMapperGenerator().AsSourceGenerator() },
                additional,
                parseOptions,
                new TestAnalyzerConfigOptionsProvider(projectDirectory ?? string.Empty));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            var emitted = updated.Emit(new MemoryStream());
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

            var pe = new MemoryStream();
            var emit = updated.Emit(pe);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            pe.Position = 0;
            var context = new AssemblyLoadContext("font-mapper-test", isCollectible: true);
            var assembly = context.LoadFromStream(pe);
            var generated = updated.SyntaxTrees
                .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .Select(tree => tree.ToString())
                .ToArray();
            return new GenerationResult(assembly, generated, diagnostics);
        }
        finally
        {
            if (fontDirectory != null)
            {
                Directory.Delete(fontDirectory, recursive: true);
            }
        }
    }

    private static IEnumerable<MetadataReference> MetadataReferences()
    {
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll" })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    private sealed class GenerationResult
    {
        public GenerationResult(Assembly assembly, string[] generated, ImmutableArray<Diagnostic> diagnostics)
        {
            Assembly = assembly;
            Generated = generated;
            Diagnostics = diagnostics;
        }

        public Assembly Assembly { get; }

        public string[] Generated { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }

    private sealed class FileAdditionalText : AdditionalText
    {
        public FileAdditionalText(string path)
        {
            Path = path;
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return SourceText.From(string.Empty, Encoding.UTF8);
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _options;

        public TestAnalyzerConfigOptionsProvider(string projectDirectory)
        {
            _options = new TestAnalyzerConfigOptions(projectDirectory);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly string _projectDirectory;

        public TestAnalyzerConfigOptions(string projectDirectory)
        {
            _projectDirectory = projectDirectory;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.projectdir")
            {
                value = _projectDirectory;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
