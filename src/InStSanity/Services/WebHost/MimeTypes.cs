namespace InStSanity.Services.WebHost;

/// <summary>
/// Content types for the files the web layer loads. Chromium is strict about a few of
/// these (scripts and stylesheets especially), so the map is explicit rather than guessed.
/// </summary>
internal static class MimeTypes
{
	const string Default = "application/octet-stream";

	static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".html"] = "text/html; charset=utf-8",
		[".htm"] = "text/html; charset=utf-8",
		[".js"] = "text/javascript; charset=utf-8",
		[".mjs"] = "text/javascript; charset=utf-8",
		[".css"] = "text/css; charset=utf-8",
		[".json"] = "application/json; charset=utf-8",
		[".map"] = "application/json; charset=utf-8",
		[".txt"] = "text/plain; charset=utf-8",
		[".md"] = "text/plain; charset=utf-8",

		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
		[".webp"] = "image/webp",
		[".avif"] = "image/avif",
		[".bmp"] = "image/bmp",
		[".ico"] = "image/x-icon",
		[".svg"] = "image/svg+xml",

		[".mp4"] = "video/mp4",
		[".m4v"] = "video/mp4",
		[".webm"] = "video/webm",
		[".mov"] = "video/quicktime",
		[".mp3"] = "audio/mpeg",
		[".m4a"] = "audio/mp4",
		[".wav"] = "audio/wav",
		[".ogg"] = "audio/ogg",

		[".woff"] = "font/woff",
		[".woff2"] = "font/woff2",
		[".ttf"] = "font/ttf",
		[".otf"] = "font/otf",
		[".wasm"] = "application/wasm",
	};

	public static string ForFile(string path)
		=> ByExtension.TryGetValue(Path.GetExtension(path), out var contentType) ? contentType : Default;

	public static string ForExtension(string extension)
		=> ByExtension.TryGetValue(extension, out var contentType) ? contentType : Default;
}
