using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools;

namespace Mobishare.App.Http;

// The Razor Pages front-end (and the chatbot's SignalR hub) call the app's own
// API over loopback HTTP (named client "CityApi") instead of in-process.
// Forward the caller's auth cookie onto that outgoing call so the
// [Authorize]-protected API controllers see the same signed-in user as the
// Razor Page / Hub method that's calling them.
public class CookieForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CookieForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Regular Razor Page -> API call: HttpContext flows normally.
        var cookie = _httpContextAccessor.HttpContext?.Request.Headers.Cookie.ToString();

        // SignalR Hub method -> API call: HttpContext isn't flowed through
        // IHttpContextAccessor, so ChatHub stashes it in this AsyncLocal instead.
        if (string.IsNullOrEmpty(cookie))
        {
            cookie = HttpClientContext.AuthCookie;
        }

        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
