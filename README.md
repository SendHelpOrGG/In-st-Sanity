# In-st-Sanity

A **.NET MAUI desktop shell for Windows**, built with the **MVVM** pattern and a **dark theme**.

It contains no features — only the structure you build on top of:

- **Side panel** — a permanently visible fly-out (`FlyoutBehavior="Locked"`) with a brand block, the navigation entry, and a footer.
- **Main page** — an intentionally empty page (plus a small placeholder) bound to its view model.

```
┌────────────────────┬───────────────────────────────────────────────┐
│  ▓ IS              │  Home                                         │
│  In-st-Sanity      │                                               │
│  Desktop workspace │                                               │
│  ────────────────  │                                               │
│  ⌂  Home           │               Nothing here yet                │
│                    │     The shell is ready. Build your content    │
│  ────────────────  │                                               │
│  v1.0.0 · Dark     │                                               │
└────────────────────┴───────────────────────────────────────────────┘
```

## Requirements

| Tool | Version |
| --- | --- |
| .NET SDK | **10.0** (project targets `net10.0-windows10.0.19041.0`) |
| MAUI workload | `dotnet workload install maui-windows` |
| OS | Windows 10 1809 (10.0.17763) or newer |
| IDE (optional) | Visual Studio 2022 17.12+ with the *.NET MAUI development* workload |

Prefer .NET 9? Change one line in `src/InStSanity/InStSanity.csproj`:

```xml
<TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
```

…and install the matching workload (`dotnet workload install maui-windows`).

## Build and run

```powershell
dotnet workload install maui-windows          # once
dotnet build InStSanity.sln                   # or: dotnet build src/InStSanity
dotnet run --project src/InStSanity
```

The app is **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`), so `dotnet run` and <kbd>F5</kbd> work directly — no MSIX, no developer certificate. The architecture-specific runtime identifier (`win-x64` / `win-arm64`) is picked up automatically from the host machine.

In Visual Studio: open `InStSanity.sln`, select the **Windows Machine** launch profile, press <kbd>F5</kbd>.

## Project structure

```
InStSanity.sln
src/InStSanity/
├─ InStSanity.csproj              Windows-only TFM, app id, unpackaged, icons
├─ MauiProgram.cs                 DI container: view models + views
├─ App.xaml(.cs)                  merges the style dictionaries, forces AppTheme.Dark, creates the window
├─ AppShell.xaml(.cs)             the side panel + main area (Shell)
├─ MainPage.xaml(.cs)             the empty main page (binds MainPageViewModel)
├─ ViewModels/
│  ├─ BaseViewModel.cs            INotifyPropertyChanged + Title/IsBusy
│  └─ MainPageViewModel.cs        view model for MainPage
├─ Resources/
│  ├─ Styles/Colors.xaml          ← the palette: re-skin the app here
│  ├─ Styles/Styles.xaml          global dark styles for every control type
│  ├─ Styles/SideBar.xaml         styles for the side panel / brand block
│  ├─ AppIcon/                    app icon (SVG background + foreground)
│  ├─ Splash/                     splash screen
│  ├─ Images/icon_home.png        side-panel icon
│  └─ Raw/                        raw assets
├─ Platforms/Windows/             WinUI entry point, manifest, DPI awareness
└─ Properties/launchSettings.json
```

## The MVVM wiring

Views and view models are resolved through the built-in dependency injection container, so no page ever constructs its own view model:

```csharp
// MauiProgram.cs
builder.Services.AddSingleton<MainPageViewModel>();
builder.Services.AddSingleton<MainPage>();
builder.Services.AddSingleton<AppShell>();
```

```csharp
// AppShell.xaml.cs — the page instance comes from DI
public AppShell(MainPage mainPage)
{
    InitializeComponent();
    HomeShellContent.Content = mainPage;
}

// MainPage.xaml.cs — the view model comes from DI
public MainPage(MainPageViewModel viewModel)
{
    InitializeComponent();
    BindingContext = viewModel;
}
```

`MainPage.xaml` uses `x:DataType="vm:MainPageViewModel"`, so its bindings are compiled and typo-proof.

### Adding a page

1. Add `ReportsPage.xaml` (+ code-behind) and a `ReportsPageViewModel : BaseViewModel` — put them next to `MainPage.*`, or in `Views/` and `ViewModels/` folders if you prefer.
2. Register both in `MauiProgram.cs` (`AddSingleton`).
3. Add to `AppShell.xaml`:

   ```xml
   <ShellContent x:Name="ReportsShellContent" Title="Reports" Icon="icon_home.png" Route="reports" />
   ```

4. Assign it in the `AppShell` constructor: `ReportsShellContent.Content = reportsPage;`

## Theming

The app ships a **single dark theme**; there are no light-mode variants to keep in sync.

- Set `UserAppTheme = AppTheme.Dark` (in `App.xaml.cs`) keeps MAUI's `AppThemeBinding` — and the native Windows chrome (title bar, fly-out pane, text boxes, scrollbars) — dark as well. `Platforms/Windows/App.xaml` additionally requests `RequestedTheme="Dark"` from WinUI.
- **All colours live in `Resources/Styles/Colors.xaml`.** Change `Primary` and `AppBackground` / `SideBarBackground` there and the whole app follows.
- `Styles.xaml` holds implicit (key-less) styles, so every `Label`, `Button`, `Entry`, `Border`, … picks them up automatically. Named styles such as `Headline`, `Subheadline`, `SecondaryButton`, `EmptyStateTitle` are opt-in via `Style="{StaticResource Headline}"`.

Want to switch to light/dark switching later? Replace the fixed colours in `Styles.xaml` with `{AppThemeBinding Light=…, Dark=…}` and drop the forced `UserAppTheme`.

## Notes

- **No third-party packages.** The MVVM base class is hand-written (`ViewModels/BaseViewModel.cs`); swap it for `CommunityToolkit.Mvvm`'s `ObservableObject` if you prefer `[ObservableProperty]` source generators.
- **No custom fonts.** The app uses the system UI font (Segoe UI on Windows); add `Resources/Fonts/*.ttf` and a `MauiFont` item in the `.csproj` if you want a bundled typeface.
- The placeholder in `MainPage.xaml` (and the matching properties in `MainPageViewModel.cs`) is the only "content" in the app — delete it once the first real view lands.
