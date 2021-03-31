// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace simdevice
{
    using CommandLine;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Parameters for the application.
    /// </summary>
    public class Parameters
    {
        /// <summary>
        /// Gets or sets the IdScope.
        /// </summary>
        [Option(
            's',
            "IdScope",
            Required = true,
            HelpText = "The Id Scope of the DPS instance")]
        public string DpsIdScope { get; set; }

        /// <summary>
        /// Gets or sets the Id.
        /// </summary>
        [Option(
            'i',
            "Id",
            Required = true,
            HelpText = "The registration Id when using individual enrollment, or the desired device Id when using group enrollment.")]
        public string deviceId { get; set; }

        //Sets device count to simulate
        [Option(
       'c',
       "Cn",
       Required = false,
       HelpText = "The registration Id when using individual enrollment, or the desired device Id when using group enrollment.")]
        public int deviceCount { get; set; }

        /// <summary>
        /// Gets or sets the modelId.
        /// </summary>
        [Option(
            'm',
            "modelId",
            Required = false,
            HelpText = "DTDL modelId of this device"
            )]
        public string modelId { get; set; }

        /// <summary>
        /// Gets or sets the PrimaryKey.
        /// </summary>
        [Option(
            'p',
            "PrimaryKey",
            Required = true,
            HelpText = "The primary key of the individual or group enrollment.")]
        public string DpsPrimaryKey { get; set; }

        /// <summary>
        /// Gets or sets the EnrollmentType.
        /// </summary>
        [Option(
            'e',
            "EnrollmentType",
            Default = EnrollmentType.Group,
            HelpText = "The type of enrollment: Individual or Group")]
        public EnrollmentType EnrollmentType { get; set; }

        /// <summary>
        /// Gets or sets the GlobalDeviceEndpoint.
        /// </summary>
        [Option(
            'g',
            "GlobalDeviceEndpoint",
            Default = "global.azure-devices-provisioning.net",
            HelpText = "The global endpoint for devices to connect to.")]
        public string GlobalDeviceEndpoint { get; set; }

        /// <summary>
        /// Gets or sets the TransportType.
        /// </summary>
        [Option(
            't',
            "TransportType",
            Default = TransportType.Mqtt,
            HelpText = "The transport to use to communicate with the device provisioning instance. Possible values include Mqtt, Mqtt_WebSocket_Only, Mqtt_Tcp_Only, Amqp, Amqp_WebSocket_Only, Amqp_Tcp_only, and Http1.")]
        public TransportType TransportType { get; set; }

        /// <summary>
        /// Gets or sets the ApplicationRunningTime.
        /// </summary>
        [Option(
         'r',
         "Application running time (in seconds)",
         Required = false,
         HelpText = "The running time for this console application. Leave it unassigned to run the application until it is explicitly canceled using Control+C.")]
        public double? ApplicationRunningTime { get; set; }

        /// <summary>
        /// The Validate.
        /// </summary>
        /// <param name="logger">The logger<see cref="ILogger"/>.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        public bool Validate(ILogger logger)
        {
            if (
                 !string.IsNullOrWhiteSpace(GlobalDeviceEndpoint)
                 && !string.IsNullOrWhiteSpace(DpsPrimaryKey)
                 && !string.IsNullOrWhiteSpace(deviceId)
                 && !string.IsNullOrWhiteSpace(DpsIdScope)

                 )
                return true;
            else return false;
        }






    }
}
