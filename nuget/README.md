# FluentUI.FontMapper

Roslyn source generator that reads a TrueType font and emits glyph string constants.

## Installation

```xml
<PackageReference Include="FluentUI.FontMapper" Version="1.0.0" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Include the font as an additional file so glyph changes rebuild the constants:

```xml
<ItemGroup>
  <AdditionalFiles Include="Assets\Fluent.ttf" />
</ItemGroup>
```

## Usage

On a partial class:

```csharp
using FluentUI.FontMapper;

namespace App.Controls;

[FontMapper("Assets/Fluent.ttf")]
public static partial class Icons
{
}
```

Or at assembly scope, with an explicit type name and namespace:

```csharp
using FluentUI.FontMapper;

[assembly: FontMapper("Assets/Fluent.ttf", "SymbolRegular", "App.Icons")]
```

Glyph names such as `ic_fluent_add_20_regular` become constants like `Add20`.

## WPF

See `examples/FluentUI.FontMapper.Examples.WPF`. Reference the generator as an analyzer, include the font as both `AdditionalFiles` and a WPF `Resource`, then bind a `TextBlock` to the generated constant:

```xml
<TextBlock FontFamily="pack://application:,,,/Assets/fontawesome.ttf#FontAwesome"
           Text="{x:Static icons:Icons.Heart}" />
```

`#` is followed by the font's family name.
