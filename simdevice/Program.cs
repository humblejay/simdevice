namespace simdevice
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Provisioning.Client;
    using Microsoft.Azure.Devices.Provisioning.Client.Transport;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using models.demoinstrument;
    using models.thermostat;
    using System.IO;
    using System.Diagnostics;

    /// <summary>
    /// Defines the <see cref="Program" />.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Defines the s_logger.
        /// </summary>
        private static ILogger s_logger;

        /// <summary>
        /// Defines the iothubConnection.
        /// </summary>
        private static string iothubConnection;

        /// <summary>
        /// Defines the modelId.
        /// </summary>
        private static Parameters parameters;
        private static string modelId;
        public static string sdeviceId;
        public static secretstore store;

        /// <summary>
        /// The Main.
        /// </summary>
        /// <param name="args">The args<see cref="string[]"/>.</param>
        /// <returns>The <see cref="Task{int}"/>.</returns>
        internal static async Task<int> Main(string[] args)
        {
            //Get Configuration from appsettings.json, environment variables and commandline
            var setConfig = GetConfiguration(args);

            parameters = new Parameters();
            setConfig.Bind(parameters);

            //If enrollment type is global, derive device key from group key
            if (parameters.EnrollmentType == EnrollmentType.Group)
                parameters.DpsPrimaryKey = ComputeDerivedSymmetricKey(parameters.DpsPrimaryKey, parameters.deviceId);

            s_logger = InitializeConsoleDebugLogger(parameters.modelId);
            store = secretstore.GetInstance(parameters);
            sdeviceId = parameters.deviceId;

            if (!parameters.Validate(s_logger))
            {
                throw new ArgumentException("Required parameters are not set. Please recheck required variables by using \"--help\"");
            }

            var runningTime = parameters.ApplicationRunningTime != null
                                ? TimeSpan.FromSeconds((double)parameters.ApplicationRunningTime)
                                : Timeout.InfiniteTimeSpan;

            s_logger.LogInformation("Press Control+C to quit the sample.");
            using var cts = new CancellationTokenSource(runningTime);

         //Check if connection string  is available in secrets.bin else provision and get connection string
                s_logger.LogInformation("Getting connection string");
                iothubConnection = GetConn(false,cts).Result;
                modelId = parameters.modelId;

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                try
                {
                    string procid = secretstore.GetSecret("procid");
             
                Process p = Process.GetProcessById(Convert.ToInt32(procid));
                    p.CloseMainWindow();
                    p.Close();
                    secretstore.SaveSecret("procid", "");
                 
                }
                catch (Exception Ex)
                {
                    //Key was not present


                }


                eventArgs.Cancel = true;
                cts.Cancel();
                s_logger.LogInformation("Sample execution cancellation requested; will exit.");
            };

            try
            {
                var status =await PerformOperations(cts);
            }
            catch (Exception ex)
            {
                //"CONNECT failed: RefusedNotAuthorized"

                if (ex.Message.ToString() == "CONNECT failed: RefusedNotAuthorized")
                {
                    iothubConnection = GetConn(true, cts).Result;

                }
            }

            return 0;
        }
        //Start Operations
        public static async Task<int> PerformOperations(CancellationTokenSource cts)
        {
            try
            {
                s_logger.LogDebug($"Set up the device client.");
                using DeviceClient deviceClient = SetupDeviceClientAsync(iothubConnection, cts.Token);

                //Check modelId and start operations
                switch (parameters.modelId)
                {
                    case "dtmi:demoapp:DemoInstrument6ql;1":
                        {
                            var sample = new DemoInstrument(deviceClient, s_logger);
                            await sample.PerformOperationsAsync(cts.Token);
                            break;
                        }
                    case "dtmi:com:example:Thermostat;1":
                        {
                            var sample = new ThermostatSample(deviceClient, s_logger);
                            await sample.PerformOperationsAsync(cts.Token);
                            break;
                        }
                }
              
            }
            catch (Exception ex)
            {
                //"CONNECT failed: RefusedNotAuthorized"
                if (ex.Message.ToString() == "CONNECT failed: RefusedNotAuthorized")
                    return 1;
                               
            }
            return 0;
        }

        /// <summary>
        /// The Get IoTHub Connection from Secure Store if saved earlier.
        /// </summary>
        /// <returns>The <see cref="string"/>.</returns>
        private static async Task<string> GetConn(Boolean renew, CancellationTokenSource cts)
        {
            var connstr = secretstore.GetSecret("iothubconn");
            if(connstr == "" || !renew)
            {
                //Provision device and get connection string
                iothubConnection = await ProvisionDeviceAsync(parameters, cts.Token);

                //Save connection string
                secretstore.SaveSecret("iothubconn", iothubConnection);
                return iothubConnection;
            }
            else
            {
                return connstr;

            }
       

        }

        /// <summary>
        /// The ProvisionDeviceAsync.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="Parameters"/>.</param>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The IoTHub Connection String/>.</returns>
        private static async Task<string> ProvisionDeviceAsync(Parameters parameters, CancellationToken cancellationToken)
        {
            SecurityProvider symmetricKeyProvider = new SecurityProviderSymmetricKey(parameters.deviceId, parameters.DpsPrimaryKey, null);
            ProvisioningTransportHandler mqttTransportHandler = new ProvisioningTransportHandlerMqtt();
            ProvisioningDeviceClient pdc = ProvisioningDeviceClient.Create(parameters.GlobalDeviceEndpoint, parameters.DpsIdScope,
                symmetricKeyProvider, mqttTransportHandler);

            var pnpPayload = new ProvisioningRegistrationAdditionalData
            {
                JsonData = $"{{ \"modelId\": \"{parameters.modelId}\" }}",
            };
            var result = await pdc.RegisterAsync(pnpPayload, cancellationToken);
            string connStr = $"HostName={result.AssignedHub};DeviceId={result.DeviceId};SharedAccessKey={parameters.DpsPrimaryKey}";
            return connStr;
        }

        /// <summary>
        /// The SetupDeviceClientAsync.
        /// </summary>
        /// <param name="conn">The conn<see cref="string"/>.</param>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="DeviceClient"/>.</returns>
        private static DeviceClient SetupDeviceClientAsync(string conn, CancellationToken cancellationToken)
        {
            DeviceClient deviceClient;

            s_logger.LogDebug($"Initializing via IoT Hub connection string");
            deviceClient = InitializeDeviceClient(conn);
            return deviceClient;
        }

        /// <summary>
        /// The InitializeDeviceClient.
        /// </summary>
        /// <param name="deviceConnectionString">The deviceConnectionString<see cref="string"/>.</param>
        /// <returns>The <see cref="DeviceClient"/>.</returns>
        private static DeviceClient InitializeDeviceClient(string deviceConnectionString)
        {
            var options = new ClientOptions
            {
                ModelId = modelId,
            };

            DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt, options);
            deviceClient.SetConnectionStatusChangesHandler((status, reason) =>
            {
                s_logger.LogDebug($"Connection status change registered - status={status}, reason={reason}.");
            });



            return deviceClient;
        }

        /// <summary>
        /// The GetConfiguration.
        /// </summary>
        /// <param name="args">The args<see cref="string[]"/>.</param>
        /// <returns>The <see cref="IConfiguration"/>.</returns>
        private static IConfiguration GetConfiguration(string[] args)
        {
            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder
                 .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                .AddEnvironmentVariables()
                .AddCommandLine(args);

            var setConfig = builder.Build();
            return setConfig;
        }

        /// <summary>
        /// The InitializeConsoleDebugLogger.
        /// </summary>
        /// <returns>The <see cref="ILogger"/>.</returns>
        private static ILogger InitializeConsoleDebugLogger(string modelId)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                .AddFilter(level => level >= LogLevel.Debug)
                .AddConsole();

            });
            
           switch(modelId)
            {
                case "dtmi:demoapp:DemoInstrument6ql;1":
                    {
                       
                        return loggerFactory.CreateLogger<DemoInstrument>();
                     
                    }
                case "dtmi:com:example:Thermostat;1":
                    {
                        return loggerFactory.CreateLogger<ThermostatSample>();
                       
                    }
                default:
                    {
                        return loggerFactory.CreateLogger<ThermostatSample>();
                    }
               
            }



        }

        /// <summary>
        /// The ComputeDerivedSymmetricKey.
        /// </summary>
        /// <param name="enrollmentKey">The enrollmentKey<see cref="string"/>.</param>
        /// <param name="deviceId">The deviceId<see cref="string"/>.</param>
        /// <returns>The <see cref="string"/>.</returns>
        private static string ComputeDerivedSymmetricKey(string enrollmentKey, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(enrollmentKey))
            {
                return enrollmentKey;
            }

            using var hmac = new HMACSHA256(Convert.FromBase64String(enrollmentKey));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(deviceId)));
        }
    }
}
