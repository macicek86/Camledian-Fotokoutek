---
name: wpf-ui
description: Working on the WPF kiosk UI (XAML, App.xaml styles, Views/, AdminView, theming, WPF UI / lepoco controls) or looking up WPF, .NET or NuGet library docs for this repo. Covers which MCP server to ask, and the App.xaml conventions and gotchas that builds do not catch.
---

# WPF kiosk UI — Camledian Photobooth

Applies to `src/Camledian.Photobooth.App` (net10.0-windows, WPF, CommunityToolkit.Mvvm,
WPF UI 4.3.0).

## Where to look things up

Route by what the question is actually about — do not answer WPF questions from memory,
and especially not WPF UI ones.

| Topic | Source |
| --- | --- |
| WPF/XAML platform semantics, resource lookup, dependency properties, app lifecycle, .NET 10 BCL, `System.Drawing.Printing` | `microsoft-learn` MCP — `microsoft_docs_search`, then `microsoft_docs_fetch` for a specific page |
| WPF UI (lepoco) — controls, `ThemesDictionary`/`ControlsDictionary`, theming, accent | `context7` MCP, library id `/lepoco/wpfui` — pass it straight to `query-docs`, no `resolve-library-id` needed |
| Other NuGet/OSS deps — OpenCvSharp, ImageSharp, CommunityToolkit.Mvvm, ONNX Runtime | `context7`, `resolve-library-id` first |

MS Learn does **not** cover WPF UI — it is third-party. When Context7 is thin on a detail,
the library's own source is the fallback: `https://raw.githubusercontent.com/lepoco/wpfui/main/src/Wpf.Ui/...`
(`Resources/Wpf.Ui.xaml` lists every control dictionary it merges).

## The app cannot be run from this devcontainer

Linux. `dotnet build` and `dotnet test` work — `EnableWindowsTargeting` is set in
`Directory.Build.props` — but the kiosk itself needs Windows. A green build proves the C# and
XAML *compiled*; it says nothing about whether the UI renders. StaticResource misses, layout and
colour are all runtime. Never report UI appearance as verified from here.

## App.xaml layout

The dictionary has a required order and three distinct zones.

1. **`MergedDictionaries` first** — `ui:ThemesDictionary Theme="Dark"` then `ui:ControlsDictionary`.
   `StaticResource` resolves in XAML document order, so anything below that references WPF UI keys
   breaks if these move down.
2. **Brand tokens and kiosk styles** — ours, all keyed: `KioskButton`, `KioskSecondaryButton`,
   `PhotoTileButton`, `ScreenTitle`, `ScreenSubtitle`, plus the palette brushes. These carry full
   custom `ControlTemplate`s on purpose: they are giant touch targets for a photobooth, not desktop
   controls. Do not rebase them on WPF UI styles.
3. **Implicit admin styles last** — layout only.

## Rules that builds do not enforce

**Never hand-write a ControlTemplate for a stock control.** That is what the WPF UI dependency
replaced (~250 lines of it). To adjust a stock control, add an implicit style carrying *layout only*
— margin, font size:

```xml
<Style TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
    <Setter Property="FontSize" Value="16" />
    <Setter Property="Margin" Value="0,0,0,12" />
</Style>
```

Same key in `TargetType` and `BasedOn` is correct, not cyclic: at parse time the style is not yet in
the dictionary, so the lookup falls through to the merged WPF UI one. WPF UI uses this pattern itself
(16 occurrences in its repo, e.g. `Wpf.Ui.Gallery/Views/Pages/Navigation/BreadcrumbBarPage.xaml`).
It works for controls whose WPF UI style is anonymous too (`Slider`, `TabControl`, `TabItem`, `Label`).

**Those implicit margins are load-bearing.** `AdminView.xaml` stacks ~60 fields in `StackPanel`s with
no margins of their own — the vertical rhythm comes entirely from the implicit styles. Deleting them
collapses the whole admin screen, and nothing in the build or tests will say so.

**Any `Button` without an explicit `Style` gets WPF UI's Fluent chrome.** For image tiles
(`SelectBackgroundView`, `SelectPhotoView`) use `Style="{StaticResource PhotoTileButton}"`, otherwise
each thumbnail picks up a rounded fill, hover highlight and accent underline.

**Accent colour has exactly one home:** `BrandAccentColor` in App.xaml, pushed into WPF UI's ~20
accent resources by `ApplyBrandAccent()` in `App.xaml.cs` `OnStartup`. Resources are parsed by
`InitializeComponent()` before `Run()` raises `OnStartup`, so `TryFindResource` is safe there.
Do **not** switch to `ApplicationThemeManager.Apply` — its `updateAccent` defaults to `true` and
overwrites the brand colour with whatever Windows accent the machine has, so the kiosk would look
different on every deployment.

**Theme is fixed Dark and never switches at runtime**, so `StaticResource` is fine for our own
brushes. WPF UI's own templates use `DynamicResource` internally; that is what makes the startup
accent override propagate.

## Checking a change

```bash
dotnet build src/Camledian.Photobooth.App/Camledian.Photobooth.App.csproj   # C# + XAML compile
dotnet test                                                                 # 97 tests, none cover UI
```

After removing any key from App.xaml, check nothing still references it — a dead `StaticResource`
is a runtime crash the build does not see:

```bash
grep -rhoP 'StaticResource \K[A-Za-z]+' src/Camledian.Photobooth.App --include=*.xaml | sort -u
```

Then have the change confirmed visually on a Windows machine.
