using AzureRelayPortBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;

namespace PortBridgeService
{
    class Program
    {
        static void Main(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder();
            var serviceCollection = new ServiceCollection();

            

            configurationBuilder
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true)
                .AddEnvironmentVariables()
                .AddCommandLine(args);

            var configuration = configurationBuilder.Build();

            serviceCollection
                .AddOptions()
                .AddLogging(c => c.AddConsole())
                .Configure<HybridConnectionServerOptions>(c => configuration.Bind("HybridConnectionServerHost", c))
                //HybridConnectionClientHost == Service Proxy 
                //HybridConnectionServerHost == Device Proxy 
                .AddSingleton<HybridConnectionServerHost>();

            var serviceProvider = serviceCollection.BuildServiceProvider();
            serviceProvider.GetService<HybridConnectionServerHost>().Run().GetAwaiter().GetResult();

            while (true)
                System.Threading.Thread.Sleep(1000);
        }
    }
}
