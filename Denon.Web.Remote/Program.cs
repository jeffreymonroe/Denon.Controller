using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Denon.Web.Remote
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Machine-specific settings (addresses, host names) go in appsettings.{Environment}.Local.json,
                    // which git ignores. It is inserted right after appsettings.{Environment}.json, so environment
                    // variables and command-line arguments still override it.
                    string localFile = $"appsettings.{context.HostingEnvironment.EnvironmentName}.Local.json";
                    int index = config.Sources.ToList().FindLastIndex(s => s is JsonConfigurationSource) + 1;
                    config.Sources.Insert(index, new JsonConfigurationSource { Path = localFile, Optional = true, ReloadOnChange = true });
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
