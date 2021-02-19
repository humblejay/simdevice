using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.DependencyInjection;
using CommandLine;
using Microsoft.Azure.Devices.Client;
using System.Threading.Tasks;


namespace simdevice
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            //Get Configuration
            var setConfig = GetConfiguration(args);


            // Parse application parameters
            Parameters parameters = null;
            ParserResult<Parameters> result = Parser.Default.ParseArguments<Parameters>(args)
                .WithParsed(parsedParams =>
                {
                    parameters = parsedParams;
                })
                .WithNotParsed(errors =>
                {
                    Environment.Exit(1);
                });

        

            var sample = new ProvisioningDeviceClientSample(parameters);
            await sample.RunSampleAsync();

            Console.WriteLine("Enter any key to exit.");
            Console.ReadKey();

            return 0;


         

        
        }

        static IConfiguration GetConfiguration(string[] args)
        {
            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder
                 .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                .AddEnvironmentVariables()
                .AddCommandLine(args);
                

            var setConfig= builder.Build();
            return setConfig;
        }
    }
}
