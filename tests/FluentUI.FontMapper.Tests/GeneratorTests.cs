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
        var font = TestFont.Create(
        [
            (0xE010, "ic_fluent_add_20_regular"),
            (0xE011, "ic_fluent_home_24_filled"),
            (0x000F0BC4, "ic_fluent_sparkle_20_regular")
        ]);
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("icons.ttf", "SymbolRegular", "Demo.Icons")]
            """;

        var compilation = Run(source, font, "icons.ttf");

        var generated = Assert.Single(compilation.Generated, text => text.Contains("class SymbolRegular"));
        Assert.Contains("namespace Demo.Icons;", generated);
        Assert.Contains("public const string Add20 = \"\\uE010\";", generated);
        Assert.Contains("public const string Home24 = \"\\uE011\";", generated);
        Assert.Contains("public const string Sparkle20 = \"\\U000F0BC4\";", generated);
        Assert.DoesNotContain("enum ", generated);

        var value = GetConstant(compilation.Assembly, "Demo.Icons.SymbolRegular", "Add20");
        Assert.Equal("\uE010", value);
        Assert.Equal("\U000F0BC4", GetConstant(compilation.Assembly, "Demo.Icons.SymbolRegular", "Sparkle20"));
    }

    [Fact]
    public void Partial_class_attribute_generates_into_the_declared_type()
    {
        var font = TestFont.Create([(0xE020, "ic_fluent_save_16_regular")]);
        var source = """
            using FluentUI.FontMapper;

            namespace App.Controls;

            [FontMapper("Assets/Fluent.ttf")]
            public static partial class Icons
            {
                public const string Manual = "manual";
            }
            """;

        var compilation = Run(source, font, "Assets/Fluent.ttf");

        var generated = Assert.Single(compilation.Generated, text => text.Contains("class Icons"));
        Assert.Contains("namespace App.Controls;", generated);
        Assert.Contains("public static partial class Icons", generated);
        Assert.Contains("public const string Save16 = \"\\uE020\";", generated);
        Assert.Equal("\uE020", GetConstant(compilation.Assembly, "App.Controls.Icons", "Save16"));
        Assert.Equal("manual", GetConstant(compilation.Assembly, "App.Controls.Icons", "Manual"));
    }

    [Fact]
    public void Both_usages_can_exist_in_one_compilation()
    {
        var font = TestFont.Create([(0xE001, "ic_fluent_add_20_regular")]);
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("icons.ttf", "SymbolFilled", "Generated")]

            namespace App;

            [FontMapper("icons.ttf")]
            internal partial class LocalIcons
            {
            }
            """;

        var compilation = Run(source, font, "icons.ttf");

        Assert.Equal("\uE001", GetConstant(compilation.Assembly, "Generated.SymbolFilled", "Add20"));
        Assert.Equal("\uE001", GetConstant(compilation.Assembly, "App.LocalIcons", "Add20"));
        Assert.Contains("internal partial class LocalIcons", Assert.Single(compilation.Generated, text => text.Contains("class LocalIcons")));
    }

    [Fact]
    public void Missing_font_reports_FM0001()
    {
        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("missing.ttf", "Icons", "Demo")]
            """;

        var result = Run(source, TestFont.Create([(0xE001, "ic_fluent_add_20_regular")]), "other.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0001");
        Assert.DoesNotContain(result.Assembly.GetTypes(), type => type.Name == "Icons");
    }

    [Fact]
    public void Non_partial_class_reports_FM0003()
    {
        var source = """
            using FluentUI.FontMapper;

            [FontMapper("icons.ttf")]
            public static class Icons
            {
            }
            """;

        var result = Run(source, TestFont.Create([(0xE001, "ic_fluent_add_20_regular")]), "icons.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0003");
    }

    [Fact]
    public void Wrong_constructor_reports_FM0007()
    {
        var source = """
            using FluentUI.FontMapper;

            [FontMapper("icons.ttf", "Name", "Namespace")]
            public static partial class Icons
            {
            }
            """;

        var result = Run(source, TestFont.Create([(0xE001, "ic_fluent_add_20_regular")]), "icons.ttf");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FM0007");
    }

    [Fact]
    public void Resolves_font_from_project_directory_without_relying_on_file_name_only()
    {
        var directory = Path.Combine(Path.GetTempPath(), "font-mapper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var relative = Path.Combine("Assets", "icons.ttf");
        var full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, TestFont.Create([(0xE030, "ic_fluent_copy_20_regular")]));
        try
        {
            var source = """
                using FluentUI.FontMapper;

                [assembly: FontMapper("Assets/icons.ttf", "Icons", "Demo")]
                """;
            var result = Run(source, fontBytes: null, additionalRelativePath: null, projectDirectory: directory);
            Assert.Equal("\uE030", GetConstant(result.Assembly, "Demo.Icons", "Copy20"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Generates_from_real_fluent_ttf()
    {
        var path = Environment.GetEnvironmentVariable("FLUENT_SYSTEM_ICONS_TTF")
            ?? @"D:\GitHub\FluentUI.SystemIcons.Assets\fluentui-system-icons\fonts\FluentSystemIcons-Regular.ttf";
        Assert.True(File.Exists(path), "Fluent System Icons Regular was not found at " + path);

        var source = """
            using FluentUI.FontMapper;

            [assembly: FontMapper("FluentSystemIcons-Regular.ttf", "SymbolRegular", "Fluent")]
            """;
        var result = Run(source, File.ReadAllBytes(path), "FluentSystemIcons-Regular.ttf");
        Assert.Equal("\uE000", GetConstant(result.Assembly, "Fluent.SymbolRegular", "AccessTime20"));
        Assert.Equal("\uE001", GetConstant(result.Assembly, "Fluent.SymbolRegular", "Accessibility32"));
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

    private static GenerationResult Run(string source, byte[]? fontBytes, string? additionalRelativePath, string? projectDirectory = null)
    {
        string? fontDirectory = null;
        string? fontPath = null;
        if (fontBytes != null)
        {
            fontDirectory = Path.Combine(Path.GetTempPath(), "font-mapper-" + Guid.NewGuid().ToString("N"));
            fontPath = Path.Combine(fontDirectory, additionalRelativePath!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
            File.WriteAllBytes(fontPath, fontBytes);
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

            var additional = fontPath == null
                ? ImmutableArray<AdditionalText>.Empty
                : ImmutableArray.Create<AdditionalText>(new FileAdditionalText(fontPath));
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
