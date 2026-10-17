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

## Run it

### Visual Studio

1. Open **`InStSanity.sln`** (not *File → Open → Folder* — folder view has no startup project).
2. Leave the solution platform on **Any CPU**. The SDK picks `win-x64` or `win-arm64` from the
   machine, so there is nothing to select there.
3. Make sure **InStSanity** is the startup project (it is the only project, so this is usually
   automatic — otherwise right-click it → *Set as Startup Project*).
4. Press <kbd>F5</kbd> to debug, or <kbd>Ctrl</kbd>+<kbd>F5</kbd> to run without the debugger.

The first build takes a few minutes: it restores the MAUI workload's packages and generates the
app icon and splash screen.

If Visual Studio offers to install missing components, accept — it needs the **.NET Multi-platform
App UI development** workload.

### Command line

```powershell
dotnet workload install maui-windows          # once per machine
dotnet run --project src/InStSanity
```

Executable after a build, if you want to double-click it:

```
src\InStSanity\bin\Debug\net10.0-windows10.0.19041.0\win-x64\InStSanity.exe
```

The app is **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`), so <kbd>F5</kbd> and
`dotnet run` work directly — no MSIX, no developer certificate.

### What you should see

1. A dark window titled **In-st-Sanity**, 1440×900.
2. Briefly a spinner and "Starting In-st-Sanity…".
3. The app UI: side panel with the In[st]Sanity brand, the library toolbar, the animated sticker.

Imported posts show fallback artwork rather than real covers until the resolution endpoints land
(phases 2–3) — that is expected, not a failure.

In a **Debug** build the Output window reports which URL the app is serving, and whether the web
layer was found:

```
[In-st-Sanity] host: http://localhost:8000/  web root: …\Resources\Raw\wwwroot  index.html: found
```

While the app is running you can also check the backend from a browser:
<http://localhost:8000/__health>. If port 8000 was busy the host moves to 8001–8020, so try
`http://localhost:8001/__health` too (the Debug output names the actual port).

### If it does not run

| Symptom | Cause and fix |
| --- | --- |
| **"The debug executable … specified in the 'Windows Machine' debug profile does not exist"** | No build has produced the exe at the path Visual Studio expects — see *that error, step by step* below. |
| `NETSDK1147: … workloads must be installed: maui-windows` | Run `dotnet workload install maui-windows`, or install the *.NET MAUI development* workload in the Visual Studio Installer. |
| `NETSDK1045: The current .NET SDK does not support targeting .NET 10.0` | The machine has no .NET 10 SDK. Install it and update Visual Studio to 17.14+ (or VS 2026), **or** switch the TFM to `net9.0-windows10.0.19041.0` in `InStSanity.csproj` and install the .NET 9 MAUI workload. |
| The project loads but shows "not supported" | Visual Studio is too old for the target framework — see the row above. |
| Pressing F5 appears to do nothing | An earlier instance is probably still running (Task Manager → `InStSanity.exe` → End task). The app allows **one** instance, because two would fight over the loopback port and the WebView2 profile; a second one exits immediately and logs why. |
| Window opens, then the overlay says the UI could not be loaded | The web layer was not found next to the executable. Check the `index.html: …` line in the Output window, and rebuild. |
| Blank window, no error | Missing WebView2 runtime. Install the *Microsoft Edge WebView2 Runtime* (Evergreen); it is preinstalled on Windows 11. |
| A port already in use, or `/__health` on 8000 fails | Something else owns 8000; the app moved to the next free port. The Debug output shows the real URL. |

To verify a build without watching the window, see the smoke test below.

### "The debug executable … does not exist"

The real build output sits one folder deeper than the path in the message — the debugger is looking
at a path no build produced:

```
bin\Debug\net10.0-windows10.0.19041.0\win-x64\InStSanity.exe     ← where the build puts it
bin\Debug\net10.0-windows10.0.19041.0\InStSanity.exe             ← what the profile asked for
```

Work through these in order:

1. **Build → Rebuild Solution**, then look at the **Error List**. A fresh clone usually needs one
   explicit build before <kbd>F5</kbd> works.
2. **If the Error List shows errors, that is the real problem** — the missing exe is only the
   symptom. The usual suspects are `NETSDK1147` (MAUI workload) and `NETSDK1045` (.NET 10 SDK);
   both are covered in the table above.
3. **If the rebuild succeeds but F5 still complains**, Visual Studio is holding a stale debug
   target. Close Visual Studio, delete `.vs`, `bin` and `obj` (repository root *and* `src/InStSanity`),
   reopen the solution, build, then <kbd>F5</kbd>.
4. **Check whether the app runs at all**, independently of the debugger:

   ```powershell
   dotnet run --project src\InStSanity
   ```

   or double-click `src\InStSanity\bin\Debug\net10.0-windows10.0.19041.0\win-x64\InStSanity.exe`.

If none of that helps, capture the last ~20 lines of **Output → Build** (not the Error List, the raw
build log) — it names the path the build actually wrote to.

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
