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

            _logger.LogDebug($"Set handler for \"EnableRemote\" command.");
            await _deviceClient.SetMethodHandlerAsync("EnableRemote", HandleEnableRemoteCommand, _deviceClient, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {

                // Generate a random value between 5.0°C and 45.0°C for the current temperature reading.
                _temperature = Math.Round(_random.NextDouble() * 40.0 + 5.0, 1);
                _humidity = _random.Next(0, 100);


                await SendTelemetryAsync();
                await Task.Delay(5 * 1000);
            }

        }

        private async Task<MethodResponse> HandleEnableRemoteCommand(MethodRequest methodRequest, object userContext)
        {
            var reportedProperties = new TwinCollection();
            reportedProperties["RemoteUrl"] = "https://test.com";

            await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
            _logger.LogDebug($"Property: Update - {{ \"RemoteUrl\":\"https://test.com\"}} is {StatusCode.Completed}.");

            var report = "{}";

            byte[] responsePayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(report));
            return new MethodResponse(responsePayload, (int)StatusCode.Completed);


        }

      

    
        

        /// <summary>
        /// The TargetTemperatureUpdateCallbackAsync.
        /// </summary>
        /// <param name="desiredProperties">The desiredProperties<see cref="TwinCollection"/>.</param>
        /// <param name="userContext">The userContext<see cref="object"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task TargetTemperatureUpdateCallbackAsync(TwinCollection desiredProperties, object userContext)
        {
            const string propertyName = "targetTemperature";

            (bool targetTempUpdateReceived, double targetTemperature) = GetPropertyFromTwin<double>(desiredProperties, propertyName);
            if (targetTempUpdateReceived)
            {
                _logger.LogDebug($"Property: Received - {{ \"{propertyName}\": {targetTemperature}°C }}.");

                string jsonPropertyPending = $"{{ \"{propertyName}\": {{ \"value\": {_temperature}, \"ac\": {(int)StatusCode.InProgress}, " +
                    $"\"av\": {desiredProperties.Version} }} }}";
                var reportedPropertyPending = new TwinCollection(jsonPropertyPending);
                await _deviceClient.UpdateReportedPropertiesAsync(reportedPropertyPending);
                _logger.LogDebug($"Property: Update - {{\"{propertyName}\": {targetTemperature}°C }} is {StatusCode.InProgress}.");

                // Update Temperature in 2 steps
                double step = (targetTemperature - _temperature) / 2d;
                for (int i = 1; i <= 2; i++)
                {
                    _temperature = Math.Round(_temperature + step, 1);
                    await Task.Delay(6 * 1000);
                }

                string jsonProperty = $"{{ \"{propertyName}\": {{ \"value\": {_temperature}, \"ac\": {(int)StatusCode.Completed}, " +
                    $"\"av\": {desiredProperties.Version}, \"ad\": \"Successfully updated target temperature\" }} }}";
                var reportedProperty = new TwinCollection(jsonProperty);
                await _deviceClient.UpdateReportedPropertiesAsync(reportedProperty);
                _logger.LogDebug($"Property: Update - {{\"{propertyName}\": {_temperature}°C }} is {StatusCode.Completed}.");
            }
            else
            {
                _logger.LogDebug($"Property: Received an unrecognized property update from service:\n{desiredProperties.ToJson()}");
            }
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
