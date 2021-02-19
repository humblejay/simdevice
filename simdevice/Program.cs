using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.DependencyInjection;


namespace simdevice
{
    class Program
    {
        static void Main(string[] args)
        {

            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .SetBasePath(Environment.CurrentDirectory)
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

        }
    }
}
