using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace FluentUI.FontGlyphs.SourceGenerator.Tests;

public class GeneratorTests
{
    [Fact]
    public void Assembly_attribute_generates_string_constants()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [assembly: FontGlyphs("fontawesome.ttf", "SymbolRegular", "Demo.Icons")]
            """;

        GenerationResult compilation = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        string generated = Assert.Single(compilation.Generated, text => text.Contains("class SymbolRegular"));
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
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            namespace App.Controls;

            [FontGlyphs("Assets/fontawesome.ttf")]
            public static partial class Icons
            {
                public const string Manual = "manual";
            }
            """;

        GenerationResult compilation = Run(source, TestFonts.FontAwesome, "Assets/fontawesome.ttf");

        string generated = Assert.Single(compilation.Generated, text => text.Contains("class Icons"));
        Assert.Contains("namespace App.Controls;", generated);
        Assert.Contains("public static partial class Icons", generated);
        Assert.Contains("public const string Heart = \"\\uF004\";", generated);
        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "App.Controls.Icons", "Heart"));
        Assert.Equal("manual", GetConstant(compilation.Assembly, "App.Controls.Icons", "Manual"));
    }

    [Fact]
    public void Both_usages_can_exist_in_one_compilation()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [assembly: FontGlyphs("fontawesome.ttf", "SymbolFilled", "Generated")]
            namespace App;

            [FontGlyphs("fontawesome.ttf")]
            internal partial class LocalIcons
            {
            }
            """;

        GenerationResult compilation = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "Generated.SymbolFilled", "Heart"));
        Assert.Equal("\uF004", GetConstant(compilation.Assembly, "App.LocalIcons", "Heart"));
        Assert.Contains("internal partial class LocalIcons", Assert.Single(compilation.Generated, text => text.Contains("class LocalIcons")));
    }

    [Fact]
    public void Missing_font_reports_FM0001()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [assembly: FontGlyphs("missing.ttf", "Icons", "Demo")]
            """;

        GenerationResult result = Run(source, TestFonts.FontAwesome, "other.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0001");
        Assert.DoesNotContain(result.Assembly.GetTypes(), type => type.Name == "Icons");
    }

    [Fact]
    public void Non_partial_class_reports_FM0003()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [FontGlyphs("fontawesome.ttf")]
            public static class Icons
            {
            }
            """;

        GenerationResult result = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0003");
    }

    [Fact]
    public void Wrong_constructor_reports_FM0007()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [FontGlyphs("fontawesome.ttf", "Name", "Namespace")]
            public static partial class Icons
            {
            }
            """;

        GenerationResult result = Run(source, TestFonts.FontAwesome, "fontawesome.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0007");
    }

    [Fact]
    public void Unnamed_font_reports_FM0002()
    {
        string source = """
            using FluentUI.FontGlyphs.SourceGenerator;

            [assembly: FontGlyphs("Segoe Fluent Icons.ttf", "Icons", "Demo")]
            """;

        GenerationResult result = Run(source, TestFonts.SegoeFluentIcons, "Segoe Fluent Icons.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0002");
        Assert.DoesNotContain(result.Assembly.GetTypes(), type => type.Name == "Icons");
    }

    [Fact]
    public void Resolves_font_from_project_directory_without_relying_on_file_name_only()
    {
        string directory = Path.Combine(Path.GetTempPath(), "font-mapper-" + Guid.NewGuid().ToString("N"));
        string relative = Path.Combine("Assets", "fontawesome.ttf");
        string full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(TestFonts.FontAwesome, full);
        try
        {
            string source = """
                using FluentUI.FontGlyphs.SourceGenerator;

                [assembly: FontGlyphs("Assets/fontawesome.ttf", "Icons", "Demo")]
                """;
            GenerationResult result = Run(source, fontPath: null, additionalRelativePath: null, projectDirectory: directory);
            Assert.Equal("\uF004", GetConstant(result.Assembly, "Demo.Icons", "Heart"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? GetConstant(Assembly assembly, string typeName, string fieldName)
    {
        Type? type = assembly.GetType(typeName);
        Assert.NotNull(type);
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
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
            CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
            SyntaxTree syntax = CSharpSyntaxTree.ParseText(source, parseOptions);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "FontGlyphs.Fixture",
                [syntax],
                MetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ImmutableArray<AdditionalText> additional = copiedFont == null
                ? []
                : ImmutableArray.Create<AdditionalText>(new FileAdditionalText(copiedFont));
            CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new FontGlyphsGenerator().AsSourceGenerator()],
                additional,
                parseOptions,
                new TestAnalyzerConfigOptionsProvider(projectDirectory ?? string.Empty));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation? updated, out ImmutableArray<Diagnostic> diagnostics);
            EmitResult emitted = updated.Emit(new MemoryStream());
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

            MemoryStream pe = new MemoryStream();
            EmitResult emit = updated.Emit(pe);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            pe.Position = 0;
            AssemblyLoadContext context = new AssemblyLoadContext("font-mapper-test", isCollectible: true);
            Assembly assembly = context.LoadFromStream(pe);
            string[] generated = updated.SyntaxTrees
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
        string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (string? name in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    private sealed class GenerationResult(Assembly assembly, string[] generated, ImmutableArray<Diagnostic> diagnostics)
    {
        public Assembly Assembly { get; } = assembly;

        public string[] Generated { get; } = generated;

        public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    private sealed class FileAdditionalText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return SourceText.From(string.Empty, Encoding.UTF8);
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider(string projectDirectory) : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _options = new(projectDirectory);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestAnalyzerConfigOptions(string projectDirectory) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.projectdir")
            {
                value = projectDirectory;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
