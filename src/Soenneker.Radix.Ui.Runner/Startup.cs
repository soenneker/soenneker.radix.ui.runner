using Microsoft.Extensions.DependencyInjection;
using Soenneker.Git.Util.Registrars;
using Soenneker.Radix.Ui.Runner.Utils;
using Soenneker.Radix.Ui.Runner.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;
using Soenneker.Playwrights.Crawler.Registrars;
using Soenneker.Utils.File.Download.Registrars;
using Soenneker.Utils.Path.Registrars;

namespace Soenneker.Radix.Ui.Runner;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.SetupIoC();
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddFileDownloadUtilAsSingleton()
                .AddGitUtilAsSingleton()
                .AddRunnersManagerAsSingleton()
                .AddPlaywrightCrawlerAsSingleton()
                .AddPathUtilAsSingleton();

        return services;
    }
}
