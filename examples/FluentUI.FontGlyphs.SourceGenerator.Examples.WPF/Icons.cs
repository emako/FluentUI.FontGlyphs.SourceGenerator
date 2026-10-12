using System.Diagnostics;
using System.Reflection;

namespace FluentUI.FontGlyphs.SourceGenerator.Examples.Wpf;

[FontGlyphs("Assets/fontawesome.ttf")]
public static partial class Icons;

#if DEBUG

[Obfuscation]
file static class DebugIcons
{
    [DebuggerStepThrough]
    static DebugIcons()
    {
        _ = Icons.Acute;
    }
}

#endif
