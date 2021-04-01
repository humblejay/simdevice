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
    using models.thermostat;


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
    

        /// <summary>
        /// The Main.
        /// </summary>
        /// <param name="args">The args<see cref="string[]"/>.</param>
        /// <returns>The <see cref="Task{int}"/>.</returns>
        internal static async Task<int> Main(string[] args)
        {
            //Get Configuration from appsettings.json, user secret, environment variables and commandline in that order
            var setConfig = GetConfiguration(args);

            parameters = new Parameters();
            setConfig.Bind(parameters);

            //This allows creating multiple devices by passing "deviceSuffix" parameter to the executable, e.g. via powershell
            parameters.deviceId = parameters.deviceId + "_" + parameters.deviceSuffix;
            sdeviceId = parameters.deviceId;

            //If enrollment type is global, derive device key from group key
            if (parameters.EnrollmentType == EnrollmentType.Group)
                parameters.DpsPrimaryKey = ComputeDerivedSymmetricKey(parameters.DpsPrimaryKey, parameters.deviceId);

            
            s_logger = InitializeConsoleDebugLogger(parameters.modelId);
            s_logger.LogInformation($"DeviceId= {sdeviceId}");


            if (!parameters.Validate(s_logger))
            {
                throw new ArgumentException("Required parameters are not set. Please recheck required variables by using \"--help\"");
            }

            var runningTime = parameters.ApplicationRunningTime != null
                                ? TimeSpan.FromSeconds((double)parameters.ApplicationRunningTime)
                                : Timeout.InfiniteTimeSpan;
            s_logger.LogInformation("Press Control+C to quit the sample.");
            using var cts = new CancellationTokenSource(runningTime);


            await RunDevice(cts);
        

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                s_logger.LogInformation("Sample execution cancellation requested; will exit.");
            };
            return 0;
        }
        public static async Task RunDevice(CancellationTokenSource cts)
        {

            s_logger.LogInformation("Getting connection string");
            iothubConnection = GetConn(false, cts).Result;

            //If GateHostName parameter is passed, append it to connection string
            //This will force device to connect via specified IoT Edge Gateway
           if(parameters.GatewayHostName != "")
            {
                iothubConnection = iothubConnection + ";GatewayHostName=" + parameters.GatewayHostName;
            }
            modelId = parameters.modelId;

            try
            {
                var status = await PerformOperations(cts);
            }
            catch (Exception ex)
            {
                //"CONNECT failed: RefusedNotAuthorized"

                if (ex.Message.ToString() == "CONNECT failed: RefusedNotAuthorized")
                {
                    //If device fails to connect to IoThub, force reprovisioning
                    await Task.Delay(3000);
                    iothubConnection = GetConn(true, cts).Result;

                }
            }


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
        /// The Get IoTHub Connection by provisioning device.
        /// This can be used to cache connection string and avoid calling DPS every time, currently not implemented
        /// </summary>
        /// <returns>The <see cref="string"/>.</returns>
        private static async Task<string> GetConn(Boolean renew, CancellationTokenSource cts)
        {
                //Provision device and get connection string
                iothubConnection = await ProvisionDeviceAsync(parameters, cts.Token);
                return iothubConnection;

        }

        /// <summary>
        /// Actual ProvisionDeviceAsync method.
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
                //Send additional data to DPS, e.g. modelId of the device
                JsonData = $"{{ \"modelId\": \"{parameters.modelId}\" }}",
            };
            var result = await pdc.RegisterAsync(pnpPayload, cancellationToken);
            //Build connection string from assigned IoThub, deviceId and Device specific Key
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
            //Builds configuration from appsettings.json, user secrets, environment variables and commandline in a sequence
            //e.g. command line configuration will override setting from previous inputs like env variables, etc.

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
        /// If new model added, update this method.  This allows same code to simulate different types of devices.
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
        /// This is used to create Device specific key from Root or Master key in DPS
        /// It is used if EnrollmentType="Group" is passed in the parameter
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
