using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Denon.Web.Api
{
    /// <summary>
    /// Rejects requests that a browser makes on behalf of some other website. CORS alone
    /// doesn't stop these: a plain GET (an image tag, a link, a form) is sent without a
    /// CORS check, and every action here is a GET. Requests from the configured origins
    /// (the web remote), same-origin requests (Swagger) and non-browser clients (curl,
    /// scripts, home automation) are let through.
    /// </summary>
    public class CrossSiteRequestGuard
    {
        private readonly RequestDelegate _next;
        private readonly string[] _allowedOrigins;

        public CrossSiteRequestGuard(RequestDelegate next, string[] allowedOrigins)
        {
            _next = next;
            _allowedOrigins = allowedOrigins ?? Array.Empty<string>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (IsCrossSite(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Cross-site request blocked",
                    Detail = "Add the calling page's origin to Security:AllowedOrigins to allow it."
                });
                return;
            }

            await _next(context);
        }

        private bool IsCrossSite(HttpRequest request)
        {
            string origin = request.Headers.Origin.ToString();
            string ownOrigin = $"{request.Scheme}://{request.Host}";

            bool originAllowed = !string.IsNullOrEmpty(origin) &&
                (string.Equals(origin, ownOrigin, StringComparison.OrdinalIgnoreCase) ||
                 _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));

            // Browsers send Origin on cross-origin script requests and on POSTs.
            if (!string.IsNullOrEmpty(origin) && !originAllowed) return true;

            // Modern browsers mark every request (including image tags and links, which
            // carry no Origin) with Sec-Fetch-Site. "cross-site" means a different site
            // started it.
            string fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase) && !originAllowed) return true;

            return false;
        }
    }
}
