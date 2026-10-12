using System.Diagnostics;
using System.Reflection;

namespace FluentUI.FontMapper.Examples.Wpf;

[FontMapper("Assets/fontawesome.ttf")]
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
