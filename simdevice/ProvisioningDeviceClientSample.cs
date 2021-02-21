﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace simdevice
{
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Provisioning.Client;
    using Microsoft.Azure.Devices.Provisioning.Client.Transport;
    using Microsoft.Azure.Devices.Shared;
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Demonstrates how to register a device with the device provisioning service using a symmetric key, and then
    /// use the registration information to authenticate to IoT Hub.
    /// </summary>
    internal class ProvisioningDeviceClientSample
    {
        /// <summary>
        /// Defines the _parameters.
        /// </summary>
        private readonly Parameters _parameters;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProvisioningDeviceClientSample"/> class.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="Parameters"/>.</param>
        public ProvisioningDeviceClientSample(Parameters parameters)
        {
            _parameters = parameters;
        }

        /// <summary>
        /// The RunSampleAsync.
        /// </summary>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        public async Task<string> RunSampleAsync()
        {
            // When registering with a symmetric key using a group enrollment, the provided key will not
            // work for a specific device, rather it must be computed based on two values: the group enrollment
            // key and the desired device Id.
            if (_parameters.EnrollmentType == EnrollmentType.Group)
            {
                _parameters.PrimaryKey = ComputeDerivedSymmetricKey(_parameters.PrimaryKey, _parameters.Id);
            }

            Console.WriteLine($"Initializing the device provisioning client...");

            // For individual enrollments, the first parameter must be the registration Id, where in the enrollment
            // the device Id is already chosen. However, for group enrollments the device Id can be requested by
            // the device, as long as the key has been computed using that value.
            // Also, the secondary could could be included, but was left out for the simplicity of this sample.
            using var security = new SecurityProviderSymmetricKey(
                _parameters.Id,
                _parameters.PrimaryKey,
                null);

            using var transportHandler = GetTransportHandler();

            ProvisioningDeviceClient provClient = ProvisioningDeviceClient.Create(
                _parameters.GlobalDeviceEndpoint,
                _parameters.IdScope,
                security,
                transportHandler);
            ProvisioningRegistrationAdditionalData pdata = new ProvisioningRegistrationAdditionalData();
            pdata.JsonData = $"{{ \"modelId\": \"{_parameters.modelId}\" }}";

            Console.WriteLine($"Initialized for registration Id {security.GetRegistrationID()}.");

            Console.WriteLine("Registering with the device provisioning service...");
            DeviceRegistrationResult result = await provClient.RegisterAsync(pdata);

            Console.WriteLine($"Registration status: {result.Status}.");
            if (result.Status != ProvisioningRegistrationStatusType.Assigned)
            {
                Console.WriteLine($"Registration status did not assign a hub, so exiting this sample.");
                return null;
            }

            Console.WriteLine($"Device {result.DeviceId} registered to {result.AssignedHub}.");

            Console.WriteLine("Creating symmetric key authentication for IoT Hub...");
            IAuthenticationMethod auth = new DeviceAuthenticationWithRegistrySymmetricKey(
                result.DeviceId,
                security.GetPrimaryKey());

            //HostName=kdemoiothub.azure-devices.net;DeviceId=ASP6025S_1;SharedAccessKey=r26+hjQnRn/RxpmoFKxV4Umw8AfuDgUxB4zpCjbumas=

            string connStr = $"HostName={result.AssignedHub};DeviceId={result.DeviceId};SharedAccessKey={security.GetPrimaryKey()}";
            return connStr;
        }

        /// <summary>
        /// Compute a symmetric key for the provisioned device from the enrollment group symmetric key used in attestation.
        /// </summary>
        /// <param name="enrollmentKey">Enrollment group symmetric key.</param>
        /// <param name="deviceId">The device Id of the key to create.</param>
        /// <returns>The key for the specified device Id registration in the enrollment group.</returns>
        private static string ComputeDerivedSymmetricKey(string enrollmentKey, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(enrollmentKey))
            {
                return enrollmentKey;
            }

            using var hmac = new HMACSHA256(Convert.FromBase64String(enrollmentKey));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(deviceId)));
        }

        /// <summary>
        /// The GetTransportHandler.
        /// </summary>
        /// <returns>The <see cref="ProvisioningTransportHandler"/>.</returns>
        private ProvisioningTransportHandler GetTransportHandler()
        {
            return _parameters.TransportType switch
            {
                TransportType.Mqtt => new ProvisioningTransportHandlerMqtt(),
                TransportType.Mqtt_Tcp_Only => new ProvisioningTransportHandlerMqtt(TransportFallbackType.WebSocketOnly),
                TransportType.Mqtt_WebSocket_Only => new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpOnly),
                TransportType.Amqp => new ProvisioningTransportHandlerAmqp(),
                TransportType.Amqp_Tcp_Only => new ProvisioningTransportHandlerAmqp(TransportFallbackType.WebSocketOnly),
                TransportType.Amqp_WebSocket_Only => new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly),
                TransportType.Http1 => new ProvisioningTransportHandlerHttp(),
                _ => throw new NotSupportedException($"Unsupported transport type {_parameters.TransportType}"),
            };
        }
    }
}
