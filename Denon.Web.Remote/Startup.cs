using Denon.Web.Remote.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Denon.Web.Remote
{
    public class Startup
    {
        public IConfiguration _configuration { get; }

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add dependency injection
            services.AddSingleton<IConfiguration>(_configuration);

            // Server-side client for the Denon Web API: connections are pooled and reused, and a
            // call that gets no answer gives up after DeviceConfig:RemoteWebApiTimeoutSeconds (default 5).
            string apiBase = _configuration["DeviceConfig:RemoteWebApi"] ?? throw new InvalidOperationException("DeviceConfig:RemoteWebApi is not set.");
            if (!apiBase.EndsWith("/")) apiBase += "/";
            services.AddHttpClient<IRemoteApiClient, RemoteApiClient>(client =>
            {
                client.BaseAddress = new Uri(apiBase);
                client.Timeout = TimeSpan.FromSeconds(_configuration.GetValue("DeviceConfig:RemoteWebApiTimeoutSeconds", 5));
            });


            services.AddControllersWithViews();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
           // app.UseHttpsRedirection();
            
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
