using System.Text.Json;

namespace InStSanity.Services.WebHost;

/// <summary>
/// A parsed HTTP request as received by <see cref="LoopbackHost"/>.
/// </summary>
public sealed class HttpRequestInfo
{
	readonly IReadOnlyDictionary<string, string> _headers;

	public HttpRequestInfo(
		string method,
		string path,
		IReadOnlyDictionary<string, string> query,
		IReadOnlyDictionary<string, string> headers,
		byte[] body)
	{
		Method = method;
		Path = path;
		Query = query;
		_headers = headers;
		Body = body;
	}

	/// <summary>HTTP verb, upper case (GET, POST, HEAD, ...).</summary>
	public string Method { get; }

	/// <summary>Decoded path without the query string, e.g. <c>/api/image</c>.</summary>
	public string Path { get; }

	/// <summary>Decoded query string values.</summary>
	public IReadOnlyDictionary<string, string> Query { get; }

	/// <summary>Request body (empty for GET requests).</summary>
	public byte[] Body { get; }

	public string? QueryValue(string name) => Query.TryGetValue(name, out var value) ? value : null;

	public string? Header(string name) => _headers.TryGetValue(name, out var value) ? value : null;

	public string? ContentType => Header("Content-Type");
}

/// <summary>
/// An HTTP response to write back to the web layer.
/// </summary>
public sealed class HttpResponseInfo
{
	static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public HttpResponseInfo(
		int status,
		string contentType,
		byte[] body,
		IReadOnlyDictionary<string, string>? headers = null)
	{
		Status = status;
		ContentType = contentType;
		Body = body;
		Headers = headers;
	}

	public int Status { get; }

	public string ContentType { get; }

	public byte[] Body { get; }

	/// <summary>Extra response headers. <c>Content-Type</c> and <c>Content-Length</c> are set automatically.</summary>
	public IReadOnlyDictionary<string, string>? Headers { get; }

	public static HttpResponseInfo Bytes(
		byte[] payload,
		string contentType,
		int status = 200,
		IReadOnlyDictionary<string, string>? headers = null)
		=> new(status, contentType, payload, headers);

	/// <summary>Serializes <paramref name="payload"/> as JSON with camelCase property names.</summary>
	public static HttpResponseInfo Json<T>(T payload, int status = 200)
		=> new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));

	public static HttpResponseInfo Text(string text, int status = 200)
		=> new(status, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(text));

	/// <summary>Standard failure shape: <c>{"ok":false,"error":"..."}</c>.</summary>
	public static HttpResponseInfo Error(string error, int status = 500)
		=> Json(new { ok = false, error }, status);
}
