namespace simdevice
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Provisioning.Client;
    using Microsoft.Azure.Devices.Provisioning.Client.Transport;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using NeoSmart.SecureStore;
    using System;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the <see cref="Program" />.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Defines the s_logger.
        /// </summary>
        private static ILogger s_logger;
        private static string iothubConnection;
        private static string modelId;
      
        /// <summary>
        /// The Main.
        /// </summary>
        /// <param name="args">The args<see cref="string[]"/>.</param>
        /// <returns>The <see cref="Task{int}"/>.</returns>
        internal static async Task<int> Main(string[] args)
        {
            //Get Configuration
            var setConfig = GetConfiguration(args);

            // Parse application parameters
            Parameters parameters = new Parameters();

            parameters.EnrollmentType = EnrollmentType.Group;
            parameters.PrimaryKey = setConfig["DpsGroupPrimaryKey"];
            parameters.IdScope = setConfig["DpsIdScope"];
            parameters.GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";
            parameters.Id = setConfig["RegistrationPrefix"];
            parameters.modelId = setConfig["ModelId"];
            parameters.TransportType = TransportType.Mqtt;

            //If enrollment type is global, derive device key from group key
            if (parameters.EnrollmentType == EnrollmentType.Group)
                parameters.PrimaryKey = ComputeDerivedSymmetricKey(parameters.PrimaryKey, parameters.Id);


            s_logger = InitializeConsoleDebugLogger();

            if (!parameters.Validate(s_logger))
            {
                throw new ArgumentException("Required parameters are not set. Please recheck required variables by using \"--help\"");
            }
            var runningTime = parameters.ApplicationRunningTime != null
                                ? TimeSpan.FromSeconds((double)parameters.ApplicationRunningTime)
                                : Timeout.InfiniteTimeSpan;

            s_logger.LogInformation("Press Control+C to quit the sample.");
            using var cts = new CancellationTokenSource(runningTime);

            try
            {
                //Check if connection string saved for use in secrets.bin
                s_logger.LogInformation("Getting saved connection string");
                iothubConnection = getConn();
                modelId = parameters.modelId;
            }
            //If secrets.bin does not exist, create it to store connection string
            catch (Exception ex)
            {

                //secrets.bin file not found, so create it
                using (var sman = SecretsManager.CreateStore())
                {
                    //securely derive key from group primary key
                    sman.LoadKeyFromPassword(parameters.PrimaryKey);
                    // Export the keyfile for future use to retrive secret
                    sman.ExportKey("secrets.key");

                    //Provision device and get connection string
                    s_logger.LogInformation("No saved connection string, adding new");
                    iothubConnection = await ProvisionDeviceAsync(parameters, cts.Token);

                    //save connection string to secure store
                    sman.Set("iothubconn", iothubConnection);

                    //save store in a file
                    sman.SaveStore("secrets.bin");

                }

            }


            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                s_logger.LogInformation("Sample execution cancellation requested; will exit.");
            };

          

              
           
           

           
            try
            {
                s_logger.LogDebug($"Set up the device client.");

                using DeviceClient deviceClient = SetupDeviceClientAsync(iothubConnection, cts.Token);
                var sample = new ThermostatSample(deviceClient, s_logger);
                await sample.PerformOperationsAsync(cts.Token);
            }catch (Exception ex)
            {
                //"CONNECT failed: RefusedNotAuthorized"
              
                if (ex.Message.ToString()== "CONNECT failed: RefusedNotAuthorized")
                {
                    s_logger.LogInformation("CONNECT failed: RefusedNotAuthorized");

                    //Reprovision device and save connection string to secure store
                    using (var sman = SecretsManager.LoadStore("secrets.bin"))
                    {
                        //Load key from file
                        sman.LoadKeyFromFile("secrets.key");
                    

                        //Provision device and get connection string
                        iothubConnection = await ProvisionDeviceAsync(parameters, cts.Token);

                        //save connection string to secure store
                        sman.Set("iothubconn", iothubConnection);
                        s_logger.LogInformation("Getting updated connection string");

                        //save store in a file
                        sman.SaveStore("secrets.bin");

                    }
                                      
                    s_logger.LogDebug($"Set up the device client.");

                    using DeviceClient deviceClient = SetupDeviceClientAsync(iothubConnection, cts.Token);
                    var sample = new ThermostatSample(deviceClient, s_logger);
                    await sample.PerformOperationsAsync(cts.Token);

                }
            }

            return 0;
        }

        /// <summary>
        /// The Get IoTHub Connection from Secure Store or from Provisioning Device
        /// </summary>
        /// <returns>The <see cref="string"/>.</returns>
        private static string getConn()
        {

            using (var sman = SecretsManager.LoadStore("secrets.bin"))
            {

                // or use an existing key file:
                sman.LoadKeyFromFile("secrets.key");
                var secret = sman.Get("iothubconn");


                return secret;

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
            SecurityProvider symmetricKeyProvider = new SecurityProviderSymmetricKey(parameters.Id, parameters.PrimaryKey, null);
            ProvisioningTransportHandler mqttTransportHandler = new ProvisioningTransportHandlerMqtt();
            ProvisioningDeviceClient pdc = ProvisioningDeviceClient.Create(parameters.GlobalDeviceEndpoint, parameters.IdScope,
                symmetricKeyProvider, mqttTransportHandler);

            var pnpPayload = new ProvisioningRegistrationAdditionalData
            {
                JsonData = $"{{ \"modelId\": \"{parameters.modelId}\" }}",
            };
            var result = await pdc.RegisterAsync(pnpPayload, cancellationToken);
            string connStr = $"HostName={result.AssignedHub};DeviceId={result.DeviceId};SharedAccessKey={parameters.PrimaryKey}";
            return connStr;

        }

        private static DeviceClient SetupDeviceClientAsync(string conn, CancellationToken cancellationToken)
        {
            DeviceClient deviceClient;
       
                    s_logger.LogDebug($"Initializing via IoT Hub connection string");
                    deviceClient = InitializeDeviceClient(conn);
            return deviceClient;
    
        }

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
        private static ILogger InitializeConsoleDebugLogger()
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                .AddFilter(level => level >= LogLevel.Debug)
                .AddConsole();

            });

            return loggerFactory.CreateLogger<ThermostatSample>();
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
