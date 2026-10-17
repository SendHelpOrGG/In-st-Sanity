# In-st-Sanity

The In[st]Sanity Instagram post saver, ported to a **.NET MAUI desktop app for Windows**.

The UI — side panel, library, 12 themes, animated sticker, synthesised sounds — is the original
web layer, served by an in-app loopback host and rendered in WebView2. Native C# provides the
backend that layer expects: media proxying, Instagram resolution, downloads and the Windows
integration. Nothing is fetched through a third-party proxy.

> **Status: Phase 1 of 5 — verified green in CI.** The native host boots, serves the web layer
> (document, range requests, `/api/*`), and the WebView phase survives startup. Media proxying
> works; post resolution, downloads and the embedded Instagram session are Phase 2/3.
> See **[docs/hybrid-port-plan.md](docs/hybrid-port-plan.md)** for the plan and
> **[docs/web-layer.md](docs/web-layer.md)** for what was changed in the web layer.

What has **not** been verified yet, because it needs eyes on a desktop: themes switching, the
sticker animating and the synthesised sounds playing. Run the app locally to confirm those.

## Requirements

| Tool | Version |
| --- | --- |
| .NET SDK | **10.0** (project targets `net10.0-windows10.0.19041.0`) |
| MAUI workload | `dotnet workload install maui-windows` |
| OS | Windows 10 1809 (10.0.17763) or newer |
| WebView2 Runtime | Evergreen — bundled with Windows 11, distributed by Windows Update on 10 |
| IDE (optional) | Visual Studio 2022 17.12+ with the *.NET MAUI development* workload |

Prefer .NET 9? Change one line in `src/InStSanity/InStSanity.csproj`
(`<TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>`) and install the matching workload.

## Build and run

```powershell
dotnet workload install maui-windows          # once
dotnet build src/InStSanity/InStSanity.csproj
dotnet run --project src/InStSanity
```

The app is **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`), so `dotnet run` and
<kbd>F5</kbd> work directly — no MSIX, no developer certificate. The runtime identifier
(`win-x64` / `win-arm64`) is picked up automatically.

Verify a build without watching the window:

```powershell
# backend only (no WebView, no desktop session needed)
pwsh -File tools/smoke-test.ps1 -Exe artifacts/InStSanity-win-x64/InStSanity.exe

# also check that the WebView comes up and the app survives it
pwsh -File tools/smoke-test.ps1 -Exe artifacts/InStSanity-win-x64/InStSanity.exe -IncludeUi
```

The script launches the app, waits for the host to answer `/__health`, then asserts that the web
document is served, that range requests work (playback needs them) and that the `/api/*` pipeline
responds. With `-IncludeUi` it launches a second time *with* the WebView and checks the app stays
alive while still serving.

Setting `INSTSANITY_HEADLESS=1` runs the app itself that way: backend up, no WebView. It is how the
native side is verified on a machine (or runner) where WebView2 or a desktop session is unavailable.

## How it fits together

```
┌─ InStSanity.exe ────────────────────────────────────────────────────────────┐
│  MAUI host            MainPage.xaml — a WebView, nothing else                │
│        │              window chrome, single instance, dark theme            │
│        │  http://localhost:8000/                                            │
│  WebView2  ← the web layer, unmodified (Resources/Raw/wwwroot)              │
│        │  fetch('/api/…')                                                   │
│  LoopbackHost (in-process TcpListener)                                      │
│        ├─ serves wwwroot + range requests                                   │
│        └─ /api/image, /api/download-photo → InstagramHttp (direct, cached)  │
└─────────────────────────────────────────────────────────────────────────────┘
```

| Component | Where |
| --- | --- |
| Loopback HTTP server | `Services/WebHost/LoopbackHost.cs` |
| API route contract | `Services/WebHost/IApiEndpoint.cs` |
| Media proxy + disk cache | `Services/Api/MediaProxyEndpoint.cs`, `Services/Instagram/MediaCache.cs` |
| Direct HTTP to Instagram | `Services/Instagram/InstagramHttp.cs` |
| Windows glue (WebView2, links, single instance) | `Platforms/Windows/` |
| The UI itself | `Resources/Raw/wwwroot/index.html` — see [docs/web-layer.md](docs/web-layer.md) |

**Why a loopback host?** The web app was built as a client for a backend on port 8000 and its
`isLocalBackendOrigin()` check treats any `localhost` origin as that backend. Serving it locally
satisfies the original contract without touching the app's call sites, keeps every request
same-origin (no CORS, full `localStorage`/IndexedDB), and keeps third-party scrapers out of the
picture. Details in [docs/hybrid-port-plan.md](docs/hybrid-port-plan.md) §2.1.

## Repository layout

```
.github/workflows/build.yml    CI: builds the Windows target, publishes the app, smoke tests it
docs/hybrid-port-plan.md       the porting plan (architecture, API contract, phases, risks)
docs/web-layer.md              where the web layer came from and what was changed in it
preview/index.html             static mock-up of the earlier native shell (see below)
tools/preview-server.py        serves the app UI in a browser, no Windows build needed
tools/patch-webapp.py          reproduces the web layer from the pristine upload
tools/smoke-test.ps1           boots a build and verifies it serves the UI
src/InStSanity/
├─ MainPage.xaml(.cs)          WebView host page
├─ MauiProgram.cs              DI: host, endpoints, view models
├─ App.xaml(.cs)               dark theme, window, starts the host
├─ AppShell.xaml(.cs)          native side-panel shell — not on the boot path, see the file header
├─ Services/                   WebHost, Api, Instagram
├─ ViewModels/                 BaseViewModel, MainPageViewModel
├─ Platforms/Windows/          WinUI entry point, WebView2 configuration, manifests
└─ Resources/Raw/wwwroot/      the web layer
```

## Previewing the UI without Windows

`tools/preview-server.py` reproduces the loopback contract with the Python standard library, so the
app UI can be opened in any browser on any OS:

```bash
python tools/preview-server.py            # http://localhost:8080/
python tools/preview-server.py --port 9000
```

| Route | Serves |
| --- | --- |
| `/` | the ported web layer — **the app's actual UI** |
| `/mockup.html` | the earlier static mock-up of the native shell |
| `/__health` | the same diagnostic payload `LoopbackHost` returns |
| `/api/*` | `501 {"ok":false,…}`, exactly as an unimplemented route answers in the app |

It is a preview tool, not the backend: no media is proxied and no post is resolved, so covers fall
back to the app's own artwork. Range requests are supported (video/audio playback needs them).

`preview/index.html` is a static mock-up of the *earlier* native shell (side panel plus an empty main
page). It is no longer what the app looks like — the ported app looks like the web layer — but it is
still handy as a colour/style reference.

## Contributing

CI is the compiler of record: Windows MAUI targets cannot be built on Linux, so every push is built,
published and smoke tested on `windows-latest`. Keep phases small and let CI prove each one.
