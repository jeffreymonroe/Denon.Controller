using Denon.Core.Model;
using Denon.Web.Remote.Models;
using Denon.Web.Remote.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Denon.Web.Remote.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IRemoteApiClient _api;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, IRemoteApiClient api)
        {
            _logger = logger;
            _configuration = configuration;
            _api = api;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            HomeViewModel model = new HomeViewModel
            {
                RemoteWebApi = _configuration["DeviceConfig:RemoteWebApi"],
                TimeoutRatio = int.Parse(_configuration["DeviceConfig:TimeoutRatio"]),
                Configuration = new ConfigurationItem(),
                RemoteVersion = typeof(HomeController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            };

            try
            {
                model.Configuration = await _api.GetConfigurationAsync(cancellationToken);
            }
            catch (RemoteApiException e)
            {
                // Still render the page, with the reason, rather than the generic error page.
                _logger.LogWarning("Couldn't load the configuration from the remote API at {Api}: {Reason}", model.RemoteWebApi, e.Message);
                model.StartupError = $"Can't load the receiver setup from the remote API at {model.RemoteWebApi} ({e.Message}).";
            }

            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
