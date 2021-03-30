// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace models.demoinstrument
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Shared;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Diagnostics;
    using System.ComponentModel;
    using System.IO;
    using simdevice;

    /// <summary>
    /// Defines the StatusCode.
    /// </summary>
    internal enum StatusCode
    {
        /// <summary>
        /// Defines the Completed.
        /// </summary>
        Completed = 200,
        /// <summary>
        /// Defines the InProgress.
        /// </summary>
        InProgress = 202,
        /// <summary>
        /// Defines the NotFound.
        /// </summary>
        NotFound = 404,
        /// <summary>
        /// Defines the BadRequest.
        /// </summary>
        BadRequest = 400
    }

    /// <summary>
    /// Defines the <see cref="DemoInstrument" />.
    /// </summary>
    public class DemoInstrument
    {
        /// <summary>
        /// Defines the _random.
        /// </summary>
        private readonly Random _random = new Random();

        /// <summary>
        /// Defines the _temperature.
        /// </summary>
        private double _temperature = 0d;

        /// <summary>
        /// Defines the _maxTemp.
        /// </summary>
        private double _humidity = 0d;


        /// <summary>
        /// Defines the _deviceClient.
        /// </summary>
        private readonly DeviceClient _deviceClient;

        /// <summary>
        /// Defines the _logger.
        /// </summary>
        private readonly ILogger _logger;

        private secretstore store;

        /// <summary>
        /// Initializes a new instance of the <see cref="DemoInstrument"/> class.
        /// </summary>
        /// <param name="deviceClient">The deviceClient<see cref="DeviceClient"/>.</param>
        /// <param name="logger">The logger<see cref="ILogger"/>.</param>
        public DemoInstrument(DeviceClient deviceClient, ILogger logger)
        {
            _deviceClient = deviceClient ?? throw new ArgumentNullException($"{nameof(deviceClient)} cannot be null.");
            _logger = logger ?? LoggerFactory.Create(builer => builer.AddConsole()).CreateLogger<DemoInstrument>();
            
        }

        /// <summary>
        /// The PerformOperationsAsync.
        /// </summary>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task PerformOperationsAsync(CancellationToken cancellationToken)
        {
            // This sample follows the following workflow:
            //Enable remote command passes connection parameters for Azure Relay connection
            //This allows remote desktop or ssh connection to the device from cloud

            _logger.LogDebug($"Set handler for \"SetRelayConfig\" command.");
            await _deviceClient.SetMethodHandlerAsync("SetRelayConfig", HandleEnableRemoteCommand, _deviceClient, cancellationToken);

            _logger.LogDebug($"Set handler to receive \"Desired Properties\" updates.");
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(HandleDesiredProperties, _deviceClient, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {

                // Generate a random value between 5.0°C and 45.0°C for the current temperature reading.
                _temperature = Math.Round(_random.NextDouble() * 40.0 + 5.0, 1);
                _humidity = _random.Next(0, 100);


                await SendTelemetryAsync();
                await Task.Delay(5 * 1000);
            }

        }

        private async Task HandleDesiredProperties(TwinCollection desiredProperties, object userContext)
        {
            const string propertyName = "RelayConnection";
            Process relayprocess;
            (bool RelayConnectionReceived, bool valueRelayConnection) = GetPropertyFromTwin<bool>(desiredProperties, propertyName);
            if (RelayConnectionReceived)
            {

                _logger.LogDebug($"Property: Received - {{ \"{propertyName}\": {valueRelayConnection} }}.");

                if (valueRelayConnection)
                {
                    string jsonPropertyPending = $"{{ \"{propertyName}\": {{ \"value\":\" {valueRelayConnection}\", \"ac\": {(int)StatusCode.InProgress}, " +
                        $"\"av\": {desiredProperties.Version} }} }}";
                    var reportedPropertyPending = new TwinCollection(jsonPropertyPending);
                    await _deviceClient.UpdateReportedPropertiesAsync(reportedPropertyPending);
                    _logger.LogDebug($"Property: Update - {{\"{propertyName}\": {valueRelayConnection} }} is {StatusCode.InProgress}.");

                     relayprocess = RelayConnection();
                    secretstore.SaveSecret("procid", relayprocess.Id.ToString());

                

                    if (!relayprocess.HasExited)
                    {
                    

                        string jsonProperty = $"{{ \"{propertyName}\": {{ \"value\": \"{valueRelayConnection}\", \"ac\": {(int)StatusCode.Completed}, " +
                            $"\"av\": {desiredProperties.Version}, \"ad\": \"Successfully enabled RDP\" }} }}";
                        var reportedProperty = new TwinCollection(jsonProperty);
                        //reportedProperty["RemoteUrl"] = "https://test.com";
                        reportedProperty["RelayConnection"] = true;
                        await _deviceClient.UpdateReportedPropertiesAsync(reportedProperty);
                        _logger.LogDebug($"Property: Update - {{\"{propertyName}\": \"{valueRelayConnection}\" }} is {StatusCode.Completed}.");
                    }
                    else
                    {
                        _logger.LogError($"Property: Update - {{\"{propertyName}\": \"{valueRelayConnection}\" }} is could not start relay.");
                    }
                }
                else
                {
                    string jsonProperty = $"{{ \"{propertyName}\": {{ \"value\":\" {valueRelayConnection}\", \"ac\": {(int)StatusCode.Completed}, " +
                      $"\"av\": {desiredProperties.Version}, \"ad\": \"Successfully disabled RDP\" }} }}";
                    var reportedProperty = new TwinCollection(jsonProperty);
                    //reportedProperty["RemoteUrl"] = "https://test.com";
                    reportedProperty["RelayConnection"] = false;

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


                    await _deviceClient.UpdateReportedPropertiesAsync(reportedProperty);
                    _logger.LogDebug($"Property: Update - {{\"{propertyName}\":\" {valueRelayConnection}\" }} is {StatusCode.Completed}.");

                }

            }
            else
            {
                _logger.LogDebug($"Property: Received an unrecognized property update from service:\n{desiredProperties.ToJson()}");
            };
        }

  

        private Process RelayConnection()
        {
            try
            {
                string jrelaconfig = secretstore.GetSecret("relayconfig");
                string sDirectory = Directory.GetCurrentDirectory();
                _logger.LogInformation(sDirectory);
                RelayConfig rc = JsonConvert.DeserializeObject<RelayConfig>(jrelaconfig);

                ProcessStartInfo sinfo = new ProcessStartInfo();
               
                sinfo.WorkingDirectory = sDirectory + "\\relay\\";
                sinfo.FileName = "PortBridgeService.exe";

                //sinfo.ArgumentList.Add("relay\\PortBridgeService.dll");
                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ServiceBusNameSpace");
                sinfo.ArgumentList.Add(rc.ServiceNameSpace);

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ServiceBusKeyName");
                sinfo.ArgumentList.Add(rc.ServiceKeyName);

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ServiceBusKey");
                sinfo.ArgumentList.Add(rc.ServiceKey);

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ForwardingRules:0:ServiceBusConnectionName");
                sinfo.ArgumentList.Add(rc.ConnectionName);

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ForwardingRules:0:TargetHostname");
                sinfo.ArgumentList.Add(rc.HostName);

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ForwardingRules:0:TargetPorts");
                sinfo.ArgumentList.Add(rc.TargetPort.ToString());

                sinfo.ArgumentList.Add("--HybridConnectionServerHost:ForwardingRules:0:InstanceCount");
                sinfo.ArgumentList.Add("1");


                sinfo.UseShellExecute = true;
                sinfo.CreateNoWindow = false;
                sinfo.ErrorDialog = true;

              
                _logger.LogInformation("Starting Process for RDP");
                Process p = Process.Start(sinfo);
 
                return p;

            }
            catch (Win32Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return null;
        }
        private async Task<MethodResponse> HandleEnableRemoteCommand(MethodRequest methodRequest, object userContext)
        {
            secretstore.SaveSecret("relayconfig",methodRequest.DataAsJson);

            RelayConfig rc = JsonConvert.DeserializeObject<RelayConfig>(methodRequest.DataAsJson);
           
        
            var reportedProperties = new TwinCollection();
            reportedProperties["RemoteUrl"] = rc.SessionUrl;

            await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
            _logger.LogDebug($"Property: Update - {{ \"RemoteUrl\":{rc.SessionUrl} is {StatusCode.Completed}.");

            var report = "{\"RelayConfig\":true}";

            byte[] responsePayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(report));
            return new MethodResponse(responsePayload, (int)StatusCode.Completed);


        }

  

        /// <summary>
        /// The GetPropertyFromTwin.
        /// </summary>
        /// <typeparam name="T">.</typeparam>
        /// <param name="collection">The collection<see cref="TwinCollection"/>.</param>
        /// <param name="propertyName">The propertyName<see cref="string"/>.</param>
        /// <returns>The <see cref="(bool, T)"/>.</returns>
        private static (bool, T) GetPropertyFromTwin<T>(TwinCollection collection, string propertyName)
        {
            return collection.Contains(propertyName) ? (true, (T)collection[propertyName]) : (false, default);
        }


        /// <summary>
        /// The SendTelemetryAsync.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task SendTelemetryAsync()
        {
            

            string telemetryPayload = $"{{ \"Temperature\": {_temperature},\"Humidity\":{_humidity}}}";
            using var message = new Message(Encoding.UTF8.GetBytes(telemetryPayload))
            {
                ContentEncoding = "utf-8",
                ContentType = "application/json",
            };

            await _deviceClient.SendEventAsync(message);
            _logger.LogDebug($"Telemetry: Sent - {{ \"Temperature\": {_temperature},\"Humidity\":{_humidity}}}");

        }


    }

    public class StartSessionReq
    {
        public string ServiceNamespace { get; set; }
        public string ServiceKeyName { get; set; }
        public string ServiceKey { get; set; }
        public string ConnectionName { get; set; }
        public string HostName { get; set; }
        public int TargetPort { get; set; }
        public string SessionUrl { get; set; }

    }
    public class StartSessionRes
    {
        public enum SessionState { 
            success,failed }

    }
}
