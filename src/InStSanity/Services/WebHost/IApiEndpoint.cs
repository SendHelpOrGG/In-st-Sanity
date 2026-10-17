namespace InStSanity.Services.WebHost;

/// <summary>
/// One route of the <c>/api/*</c> backend the web layer expects.
/// </summary>
/// <remarks>
/// The original app talked to a local backend on port 8000 (see
/// <c>docs/hybrid-port-plan.md</c> section 4). Each route is implemented as an
/// endpoint class and registered in <c>MauiProgram</c>; <see cref="LoopbackHost"/>
/// dispatches to them. Routes that are not registered yet answer
/// <c>501 not_implemented</c>, which the web layer already handles gracefully.
/// </remarks>
public interface IApiEndpoint
{
	/// <summary>Exact request path this endpoint serves, e.g. <c>/api/image</c>.</summary>
	string Route { get; }

	Task<HttpResponseInfo> HandleAsync(HttpRequestInfo request, CancellationToken cancellationToken);
}
