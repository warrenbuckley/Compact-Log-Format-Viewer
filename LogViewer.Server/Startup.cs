using LogViewer.Server.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            services.AddControllers().AddNewtonsoftJson();
            services.AddSingleton<ILogParser, LogParser>();
            services.AddSignalR();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseDefaultFiles(); // index.html etc
            app.UseStaticFiles(); // serve assets from wwwroot

            app.UseDeveloperExceptionPage();
            
            app.UseRouting();
            app.UseEndpoints(routes =>
            {
                routes.MapControllers();
                routes.MapHub<LogHub>("log");
            });
        }
    }
}
