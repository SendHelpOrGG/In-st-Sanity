using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace InStSanity.Services.WebHost;

/// <summary>
/// In-process HTTP server that serves the ported web app (its <c>wwwroot</c> files) and
/// the <c>/api/*</c> backend that goes with it.
/// </summary>
/// <remarks>
/// <para>
/// The web layer was written as a client for a local backend: its
/// <c>isLocalBackendOrigin()</c> check treats any <c>localhost</c> origin as that backend,
/// and its API calls are relative paths such as <c>/api/resolve-post</c>. Serving the app
/// from this host therefore satisfies the original contract without patching the call
/// sites, keeps everything same-origin (no CORS, full <c>localStorage</c>/IndexedDB access)
/// and keeps the network surface local — nothing is requested from third-party services.
/// </para>
/// <para>
/// Deliberately small: one request per connection, <c>Connection: close</c>, no chunked
/// encoding. Keep-alive is a later optimisation (see the plan's Phase 4).
/// </para>
/// </remarks>
public sealed class LoopbackHost : IAsyncDisposable
{
	/// <summary>Port the web layer prefers (the original local backend used it).</summary>
	public const int DefaultPort = 8000;

	const int PortProbeCount = 20;
	const int MaxHeaderBytes = 32 * 1024;
	const int MaxBodyBytes = 64 * 1024 * 1024;
	const int AcceptBacklog = 64;
	static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
	static readonly byte[] HeadTerminator = { 13, 10, 13, 10 };

	readonly IReadOnlyDictionary<string, IApiEndpoint> _endpoints;
	readonly ILogger<LoopbackHost> _logger;
	readonly TcpListener _listener;
	readonly CancellationTokenSource _shutdown = new();
	Task? _acceptLoop;

	public LoopbackHost(IEnumerable<IApiEndpoint> endpoints, ILogger<LoopbackHost> logger)
	{
		_logger = logger;
		_endpoints = endpoints.ToDictionary(endpoint => endpoint.Route, StringComparer.OrdinalIgnoreCase);
		WebRoot = ResolveWebRoot();

		var (listener, port) = Bind();
		_listener = listener;
		Port = port;
	}

	/// <summary>Directory the static web app is served from.</summary>
	public string WebRoot { get; }

	/// <summary>Port the server listens on — <see cref="DefaultPort"/> unless it was taken.</summary>
	public int Port { get; }

	/// <summary>
	/// URL to point the WebView at. The host name must stay <c>localhost</c>: the web layer
	/// treats a localhost origin as its own backend regardless of the port.
	/// </summary>
	public string Url => $"http://localhost:{Port}/";

	/// <summary>Starts serving. Returns once the listener is accepting connections.</summary>
	public void Start()
	{
		if (_acceptLoop is not null)
		{
			return;
		}

		_acceptLoop = Task.Run(AcceptLoopAsync);
		_logger.LogInformation("Web host serving {Root} on {Url}", WebRoot, Url);
	}

	public async ValueTask DisposeAsync()
	{
		await _shutdown.CancelAsync().ConfigureAwait(false);

		try
		{
			_listener.Stop();
		}
		catch (SocketException)
		{
			// already stopped
		}

		if (_acceptLoop is not null)
		{
			try
			{
				await _acceptLoop.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// expected on shutdown
			}
		}

		_shutdown.Dispose();
	}

	// ── listener ────────────────────────────────────────────────────────────────

	static (TcpListener Listener, int Port) Bind(int preferredPort = DefaultPort)
	{
		for (var offset = 0; offset <= PortProbeCount; offset++)
		{
			var port = preferredPort + offset;
			try
			{
				var listener = new TcpListener(IPAddress.Loopback, port);
				listener.Start(AcceptBacklog);
				return (listener, port);
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
			{
				// try the next port
			}
		}

		// Nothing in the range was free: let the OS pick.
		var ephemeral = new TcpListener(IPAddress.Loopback, 0);
		ephemeral.Start(AcceptBacklog);
		var assigned = ((IPEndPoint)ephemeral.LocalEndpoint).Port;
		return (ephemeral, assigned);
	}

	static string ResolveWebRoot()
	{
		var baseDirectory = AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, "wwwroot"),
			Path.Combine(baseDirectory, "Resources", "Raw", "wwwroot"),
		};

		foreach (var candidate in candidates)
		{
			if (File.Exists(Path.Combine(candidate, "index.html")))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return Path.GetFullPath(candidates[0]);
	}

	async Task AcceptLoopAsync()
	{
		var token = _shutdown.Token;

		while (!token.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (SocketException ex)
			{
				_logger.LogDebug(ex, "Accept failed");
				continue;
			}

			_ = HandleClientAsync(client);
		}
	}

	// ── request handling ────────────────────────────────────────────────────────

	async Task HandleClientAsync(TcpClient client)
	{
		using (client)
		{
			client.NoDelay = true;

			try
			{
				using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
				timeout.CancelAfter(RequestTimeout);
				var token = timeout.Token;

				await using var stream = client.GetStream();

				var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
				if (request is null)
				{
					return;
				}

				var response = await RouteAsync(request, token).ConfigureAwait(false);
				await WriteResponseAsync(stream, request.Method, response, token).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
			{
				// The client went away or the request timed out; nothing to report.
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Request failed");
			}
		}
	}

	async Task<HttpResponseInfo> RouteAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Path.Equals("/__health", StringComparison.OrdinalIgnoreCase))
		{
			return HttpResponseInfo.Json(new
			{
				ok = true,
				port = Port,
				url = Url,
				webRoot = WebRoot,
				webRootExists = Directory.Exists(WebRoot),
				endpoints = _endpoints.Keys.OrderBy(route => route, StringComparer.OrdinalIgnoreCase).ToArray(),
				uptimeSeconds = (int)Stopwatch.GetElapsedTime(_startedAt).TotalSeconds,
			});
		}

		if (request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
		{
			if (!_endpoints.TryGetValue(request.Path, out var endpoint))
			{
				return HttpResponseInfo.Json(
					new { ok = false, error = "not_implemented", route = request.Path },
					501);
			}

			try
			{
				return await endpoint.HandleAsync(request, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "API route {Route} failed", request.Path);
				return HttpResponseInfo.Error("internal_error");
			}
		}

		return await ServeStaticAsync(request, cancellationToken).ConfigureAwait(false);
	}

	readonly long _startedAt = Stopwatch.GetTimestamp();

	async Task<HttpResponseInfo> ServeStaticAsync(HttpRequestInfo request, CancellationToken cancellationToken)
	{
		if (request.Method is not ("GET" or "HEAD"))
		{
			return HttpResponseInfo.Text("Only GET and HEAD are supported.", 405);
		}

		var relativePath = request.Path.TrimStart('/');
		if (relativePath.Length == 0)
		{
			relativePath = "index.html";
		}

		var fullPath = Path.GetFullPath(
			Path.Combine(WebRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

		// Path traversal guard: the resolved file has to stay inside the web root.
		if (!fullPath.StartsWith(WebRoot, StringComparison.OrdinalIgnoreCase))
		{
			return HttpResponseInfo.Text("Forbidden.", 403);
		}

		if (!File.Exists(fullPath))
		{
			return HttpResponseInfo.Text($"Not found: {request.Path}", 404);
		}

		byte[] content;
		try
		{
			content = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
		}
		catch (IOException ex)
		{
			_logger.LogWarning(ex, "Could not read {Path}", fullPath);
			return HttpResponseInfo.Text("Could not read file.", 500);
		}

		var contentType = MimeTypes.ForFile(fullPath);
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Accept-Ranges"] = "bytes",
			// The web layer is rebuilt while developing, so keep the document fresh.
			["Cache-Control"] = Path.GetFileName(fullPath).Equals("index.html", StringComparison.OrdinalIgnoreCase)
				? "no-cache"
				: "public, max-age=3600",
		};

		if (TryParseRange(request.Header("Range"), content.LongLength, out var start, out var end))
		{
			var length = (int)(end - start + 1);
			var slice = new byte[length];
			Array.Copy(content, start, slice, 0, length);

			headers["Content-Range"] = $"bytes {start}-{end}/{content.LongLength}";
			return HttpResponseInfo.Bytes(slice, contentType, 206, headers);
		}

		return HttpResponseInfo.Bytes(content, contentType, 200, headers);
	}

	/// <summary>Parses a single-range <c>bytes=</c> header. Multiple ranges fall back to a full response.</summary>
	internal static bool TryParseRange(string? header, long length, out long start, out long end)
	{
		start = 0;
		end = length - 1;

		if (string.IsNullOrWhiteSpace(header) || length <= 0)
		{
			return false;
		}

		if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var spec = header[6..].Trim();
		if (spec.Contains(','))
		{
			return false;
		}

		var dash = spec.IndexOf('-');
		if (dash < 0)
		{
			return false;
		}

		var startText = spec[..dash].Trim();
		var endText = spec[(dash + 1)..].Trim();

		if (startText.Length == 0)
		{
			// Suffix range: the last N bytes.
			if (!long.TryParse(endText, out var suffix) || suffix <= 0)
			{
				return false;
			}

			start = Math.Max(0, length - suffix);
			end = length - 1;
			return true;
		}

		if (!long.TryParse(startText, out start) || start < 0 || start >= length)
		{
			return false;
		}

		if (endText.Length > 0)
		{
			if (!long.TryParse(endText, out end))
			{
				return false;
			}

			if (end >= length)
			{
				end = length - 1;
			}
		}
		else
		{
			end = length - 1;
		}

		return start <= end;
	}

	// ── wire format ─────────────────────────────────────────────────────────────

	static async Task<HttpRequestInfo?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		var accumulated = new MemoryStream();
		var buffer = new byte[8192];

		while (accumulated.Length < MaxHeaderBytes)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (read <= 0)
			{
				return null;
			}

			accumulated.Write(buffer, 0, read);

			var data = accumulated.GetBuffer();
			var length = (int)accumulated.Length;
			var headEnd = IndexOf(data, length, HeadTerminator);
			if (headEnd < 0)
			{
				continue;
			}

			var head = Encoding.ASCII.GetString(data, 0, headEnd);
			var overflow = length - (headEnd + HeadTerminator.Length);
			var rest = new byte[overflow];
			if (overflow > 0)
			{
				Array.Copy(data, headEnd + HeadTerminator.Length, rest, 0, overflow);
			}

			return await ParseRequestAsync(head, rest, stream, cancellationToken).ConfigureAwait(false);
		}

		return null;
	}

	static async Task<HttpRequestInfo?> ParseRequestAsync(
		string head,
		byte[] alreadyRead,
		NetworkStream stream,
		CancellationToken cancellationToken)
	{
		using var reader = new StringReader(head);

		var requestLine = reader.ReadLine();
		if (string.IsNullOrWhiteSpace(requestLine))
		{
			return null;
		}

		var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2)
		{
			return null;
		}

		var method = parts[0].ToUpperInvariant();
		var target = parts[1];

		var queryStart = target.IndexOf('?');
		var rawPath = queryStart >= 0 ? target[..queryStart] : target;
		var query = queryStart >= 0
			? ParseQuery(target[(queryStart + 1)..])
			: EmptyQuery;

		var path = Uri.UnescapeDataString(rawPath);

		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string? line;
		while ((line = reader.ReadLine()) is not null)
		{
			if (line.Length == 0)
			{
				continue;
			}

			var colon = line.IndexOf(':');
			if (colon <= 0)
			{
				continue;
			}

			headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
		}

		var contentLength = 0;
		if (headers.TryGetValue("Content-Length", out var lengthText))
		{
			int.TryParse(lengthText, out contentLength);
		}

		if (contentLength > MaxBodyBytes)
		{
			return null;
		}

		var body = await ReadBodyAsync(stream, alreadyRead, contentLength, cancellationToken).ConfigureAwait(false);

		return new HttpRequestInfo(method, path, query, headers, body);
	}

	static async Task<byte[]> ReadBodyAsync(
		NetworkStream stream,
		byte[] alreadyRead,
		int contentLength,
		CancellationToken cancellationToken)
	{
		if (contentLength <= 0)
		{
			return Array.Empty<byte>();
		}

		var body = new byte[contentLength];
		var copied = Math.Min(alreadyRead.Length, contentLength);
		Array.Copy(alreadyRead, body, copied);

		var offset = copied;
		while (offset < contentLength)
		{
			var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken)
				.ConfigureAwait(false);
			if (read <= 0)
			{
				break;
			}

			offset += read;
		}

		return body;
	}

	static readonly IReadOnlyDictionary<string, string> EmptyQuery =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	static IReadOnlyDictionary<string, string> ParseQuery(string queryString)
	{
		var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var equals = pair.IndexOf('=');
			var key = equals >= 0 ? pair[..equals] : pair;
			var value = equals >= 0 ? pair[(equals + 1)..] : string.Empty;

			var decodedKey = Uri.UnescapeDataString(key.Replace('+', ' '));
			if (decodedKey.Length == 0)
			{
				continue;
			}

			query[decodedKey] = Uri.UnescapeDataString(value.Replace('+', ' '));
		}

		return query;
	}

	static int IndexOf(byte[] haystack, int length, byte[] needle)
	{
		for (var i = 0; i <= length - needle.Length; i++)
		{
			var found = true;
			for (var j = 0; j < needle.Length; j++)
			{
				if (haystack[i + j] != needle[j])
				{
					found = false;
					break;
				}
			}

			if (found)
			{
				return i;
			}
		}

		return -1;
	}

	static async Task WriteResponseAsync(
		NetworkStream stream,
		string method,
		HttpResponseInfo response,
		CancellationToken cancellationToken)
	{
		var header = new StringBuilder();
		header.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(ReasonPhrase(response.Status)).Append("\r\n");
		header.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
		header.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");

		if (response.Headers is not null)
		{
			foreach (var (name, value) in response.Headers)
			{
				header.Append(name).Append(": ").Append(value).Append("\r\n");
			}
		}

		header.Append("Connection: close\r\n\r\n");

		var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
		await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

		var sendsBody = method is not "HEAD" && response.Status is not (204 or 304) && response.Body.Length > 0;
		if (sendsBody)
		{
			await stream.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
		}

		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	static string ReasonPhrase(int status) => status switch
	{
		200 => "OK",
		204 => "No Content",
		206 => "Partial Content",
		304 => "Not Modified",
		400 => "Bad Request",
		403 => "Forbidden",
		404 => "Not Found",
		405 => "Method Not Allowed",
		416 => "Range Not Satisfiable",
		500 => "Internal Server Error",
		501 => "Not Implemented",
		502 => "Bad Gateway",
		504 => "Gateway Timeout",
		_ => "Status",
	};
}
