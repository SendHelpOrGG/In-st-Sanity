using Microsoft.Extensions.DependencyInjection;
using InStSanity.ViewModels;

namespace InStSanity;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder.UseMauiApp<App>();

		// ── MVVM service registration ────────────────────────────────────────
		// Views and view models are resolved through the built-in DI container,
		// so both can take their dependencies through the constructor.
		//
		//   App            ← AppShell ← MainPage ← MainPageViewModel
		//
		// Add services, repositories and further pages/view models here.
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<AppShell>();

		return builder.Build();
	}
}
