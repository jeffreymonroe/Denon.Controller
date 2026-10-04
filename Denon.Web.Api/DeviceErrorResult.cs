using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Denon.Web.Api
{
    /// <summary>
    /// Turns a receiver failure recorded during the action into a 503 Problem response.
    /// The controller catches telnet failures itself and records them here rather than
    /// letting the exception escape the action, so a debugger doesn't stop on every request
    /// while the receiver is off, and the caller gets a clear message instead of a bare 500.
    /// </summary>
    public sealed class DeviceErrorResultAttribute : ResultFilterAttribute
    {
        public const string ItemKey = "Denon.DeviceError";

        public override void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.HttpContext.Items.TryGetValue(ItemKey, out object message))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = message as string,
                })
                { StatusCode = StatusCodes.Status503ServiceUnavailable };
            }
        }
    }
}
