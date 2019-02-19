using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LogViewer.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
        }

        public void Configure(IApplicationBuilder app)
        {
            var serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
            
            app.UseDefaultFiles(); // index.html etc
            app.UseStaticFiles(); // serve assets from wwwroot

            app.UseDeveloperExceptionPage();
            app.UseMvc(); // WebAPI MVC Routing
 
            //app.Run(async (context) =>
            //{
            //    context.Response.ContentType = "text/html";
            //    await context.Response
            //        .WriteAsync("<!DOCTYPE html><html lang=\"en\"><head>" +
            //            "<title></title></head><body><p>Hosted by Kestrel</p>");

            //    if (serverAddressesFeature != null)
            //    {
            //        await context.Response
            //            .WriteAsync("<p>Listening on the following addresses: " +
            //                string.Join(", ", serverAddressesFeature.Addresses) +
            //                "</p>");
            //    }

            //    await context.Response.WriteAsync("<p>Request URL: " +
            //        $"{context.Request.GetDisplayUrl()}<p>");
            //});
        }
    }
}
