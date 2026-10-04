using Denon.Core.Command;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using System;
using System.Text.Json.Serialization;

namespace Denon.Web.Api
{
    public class Startup
    {
        public IConfiguration _configuration { get; }
        private IDenonDevice _deviceConfig { get; }
        private IDenonConnection _connection { get; }

        private readonly string _policyName = "CorsPolicy";
        private readonly string[] _allowedOrigins;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;

            string filename = System.IO.Path.Combine(AppContext.BaseDirectory, "Schemas", configuration["DeviceConfig:ModelFilename"]);

            if (!System.IO.File.Exists(filename)) throw new FieldAccessException(String.Format("File ({0}) does not exist.", filename));

            _deviceConfig = new DenonDevice(filename, configuration["DeviceConfig:Host"]);
            // Telnet or serial, from DeviceConfig:ConnectionType. Created here so a bad setting
            // stops the API at startup rather than on the first request.
            _connection = DenonConnectionFactory.Create(configuration);

            // Web pages (other than this API's own) allowed to call the API from a browser.
            _allowedOrigins = configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        }


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add dependency injection
            services.AddSingleton<IConfiguration>(_configuration);
            services.AddSingleton<IDenonDevice>(_deviceConfig);
            // Registered through a factory so the container disposes it (closing the serial
            // port or telnet connection) when the API shuts down.
            services.AddSingleton<IDenonConnection>(_ => _connection);


            //services.AddCors(options =>
            //{
            //    options.AddDefaultPolicy(builder =>
            //        {
            //            //builder.WithOrigins("localhost:44387")
            //            //                .AllowAnyHeader()
            //            //                .AllowAnyMethod();
            //            builder.WithOrigins("https://localhost:5001", "http://localhost:5000")
            //.AllowAnyMethod()
            //.AllowAnyHeader();
            //        });
            //});
            services.AddCors(opt =>
            {
                opt.AddPolicy(name: _policyName, builder =>
                {
                    builder.WithOrigins(_allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });


            services.AddControllers()
                .AddJsonOptions(options =>  options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Denon Web API Public", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            logger.LogInformation("Receiver connection: {Connection}", _connection.Description);

            if (env.IsDevelopment())
            {
                //app.UseDeveloperExceptionPage();
                app.UseExceptionHandler("/error-local-development");
            }
            else
            {
                app.UseExceptionHandler("/error");
            }


            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Denon Web API Public V1");
            });

            //app.UseHttpsRedirection();

            app.UseRouting();

            // Block requests other websites make through the user's browser.
            app.UseMiddleware<CrossSiteRequestGuard>(new object[] { _allowedOrigins });

            app.UseCors(_policyName);
            //app.UseCors(options => options.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
