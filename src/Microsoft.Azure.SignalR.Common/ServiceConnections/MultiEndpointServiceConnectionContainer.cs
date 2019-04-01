﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR.Common;
using Microsoft.Azure.SignalR.Common.ServiceConnections;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.SignalR
{
    internal class MultiEndpointServiceConnectionContainer : IServiceConnectionContainer
    {
        private readonly IServiceEndpointManager _endpointManager;
        private readonly IMessageRouter _router;
        private readonly ILogger _logger;
        private readonly IServiceConnectionContainer _inner;

        public Dictionary<ServiceEndpoint, IServiceConnectionContainer> Connections { get; }

        public MultiEndpointServiceConnectionContainer(Func<ServiceEndpoint, IServiceConnectionContainer> generator, IServiceEndpointManager endpointManager, IMessageRouter router, ILoggerFactory loggerFactory)
        {
            if (generator == null)
            {
                throw new ArgumentNullException(nameof(generator));
            }

            _endpointManager = endpointManager ?? throw new ArgumentNullException(nameof(endpointManager));
            _logger = loggerFactory?.CreateLogger<MultiEndpointServiceConnectionContainer>() ?? NullLogger<MultiEndpointServiceConnectionContainer>.Instance;

            var endpoints = endpointManager.Endpoints;

            if (endpoints.Length == 1)
            {
                _inner = generator(endpoints[0]);
            }
            else
            {
                // router is required when endpoints > 1
                _router = router ?? throw new ArgumentNullException(nameof(router));
                Connections = endpoints.ToDictionary(s => s, s => generator(s));
            }
        }

        public MultiEndpointServiceConnectionContainer(IServiceConnectionFactory serviceConnectionFactory, string hub,
            int count, IServiceEndpointManager endpointManager, IMessageRouter router, ILoggerFactory loggerFactory)
        :this(endpoint => CreateContainer(serviceConnectionFactory, endpoint, hub, count, endpointManager, loggerFactory),
            endpointManager, router, loggerFactory)
        {
        }

        private static IServiceConnectionContainer CreateContainer(IServiceConnectionFactory serviceConnectionFactory, ServiceEndpoint endpoint, string hub, int count, IServiceEndpointManager endpointManager, ILoggerFactory loggerFactory)
        {
            var provider = endpointManager.GetEndpointProvider(endpoint);
            var connectionFactory = new ConnectionFactory(hub, provider, loggerFactory);
            if (endpoint.EndpointType == EndpointType.Primary)
            {
                return new StrongServiceConnectionContainer(serviceConnectionFactory, connectionFactory, count, endpoint);
            }
            else
            {
                return new WeakServiceConnectionContainer(serviceConnectionFactory, connectionFactory, count, endpoint);
            }
        }

        public ServiceConnectionStatus Status => throw new NotSupportedException();

        public Task ConnectionInitializedTask
        {
            get
            {
                if (_inner != null)
                {
                    return _inner.ConnectionInitializedTask;
                }

                return Task.WhenAll(from connection in Connections
                                    select connection.Value.ConnectionInitializedTask);
            }
        }

        public Task StartAsync()
        {
            if (_inner != null)
            {
                return _inner.StartAsync();
            }

            return Task.WhenAll(Connections.Select(s =>
            {
                Log.StartingConnection(_logger, s.Key.Endpoint);
                return s.Value.StartAsync();
            }));
        }

        public Task StopAsync()
        {
            if (_inner != null)
            {
                return _inner.StopAsync();
            }

            return Task.WhenAll(Connections.Select(s =>
            {
                Log.StoppingConnection(_logger, s.Key.Endpoint);
                return s.Value.StopAsync();
            }));
        }

        public Task WriteAsync(ServiceMessage serviceMessage)
        {
            if (_inner != null)
            {
                return _inner.WriteAsync(serviceMessage);
            }

            var routed = GetRoutedEndpoints(serviceMessage, _endpointManager.GetAvailableEndpoints()).ToArray();

            if (routed.Length == 0)
            {
                throw new AzureSignalRNotConnectedException();
            }

            return Task.WhenAll(routed.Select(s => Connections[s]).Select(s => s.WriteAsync(serviceMessage)));
        }

        public async Task<bool> WriteAckableMessageAsync(ServiceMessage serviceMessage, CancellationToken cancellationToken = default)
        {
            if (_inner != null)
            {
                return await _inner.WriteAckableMessageAsync(serviceMessage, cancellationToken);
            }

            var routed = GetRoutedEndpoints(serviceMessage, _endpointManager.GetAvailableEndpoints()).ToArray();

            if (routed.Length == 0)
            {
                throw new AzureSignalRNotConnectedException();
            }

            // If we have multiple endpoints, we should wait to one of the following conditions hit
            // 1. One endpoint responses "OK" state
            // 2. All the endpoints response failed state including "NotFound", "Timeout" and waiting response to timeout
            var tasks = new List<Task>();
            var containers = routed.Select(s => Connections[s]);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            foreach (var container in containers)
            {
                tasks.Add(WriteAndVerifyAckableMessageAsync(container, serviceMessage, tcs, cancellationToken));
            }

            // If tcs.Task completes, one Endpoint responses "OK" state.
            var task = await Task.WhenAny(tcs.Task, Task.WhenAll(tasks));

            // This will throw exceptions in tasks if exceptions exist
            await task;

            return tcs.Task.IsCompleted;
        }

        private async Task WriteAndVerifyAckableMessageAsync(IServiceConnectionContainer container, ServiceMessage serviceMessage,
            TaskCompletionSource<bool> tcs, CancellationToken cancellationToken)
        {
            var succeeded = await container.WriteAckableMessageAsync(serviceMessage, cancellationToken);
            if (succeeded)
            {
                tcs.TrySetResult(true);
            }
        }

        internal IEnumerable<ServiceEndpoint> GetRoutedEndpoints(ServiceMessage message, IEnumerable<ServiceEndpoint> availableEndpoints)
        {
            switch (message)
            {
                case BroadcastDataMessage bdm:
                    return _router.GetEndpointsForBroadcast(availableEndpoints);
                case GroupBroadcastDataMessage gbdm:
                    return _router.GetEndpointsForGroup(gbdm.GroupName, availableEndpoints);
                case JoinGroupWithAckMessage jgm:
                    return _router.GetEndpointsForGroup(jgm.GroupName, availableEndpoints);
                case LeaveGroupWithAckMessage lgm:
                    return _router.GetEndpointsForGroup(lgm.GroupName, availableEndpoints);
                case MultiGroupBroadcastDataMessage mgbdm:
                    return mgbdm.GroupList.SelectMany(g => _router.GetEndpointsForGroup(g, availableEndpoints)).Distinct();
                case ConnectionDataMessage cdm:
                    return _router.GetEndpointsForConnection(cdm.ConnectionId, availableEndpoints);
                case MultiConnectionDataMessage mcd:
                    return mcd.ConnectionList.SelectMany(c => _router.GetEndpointsForConnection(c, availableEndpoints)).Distinct();
                case UserDataMessage udm:
                    return _router.GetEndpointsForUser(udm.UserId, availableEndpoints);
                case MultiUserDataMessage mudm:
                    return mudm.UserList.SelectMany(g => _router.GetEndpointsForUser(g, availableEndpoints)).Distinct();
                default:
                    throw new NotSupportedException(message.GetType().Name);
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception> _startingConnection =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "StartingConnection"), "Staring connections for endpoint {endpoint}");

            private static readonly Action<ILogger, string, Exception> _stoppingConnection =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "StoppingConnection"), "Stopping connections for endpoint {endpoint}");

            public static void StartingConnection(ILogger logger, string endpoint)
            {
                _startingConnection(logger, endpoint, null);
            }

            public static void StoppingConnection(ILogger logger, string endpoint)
            {
                _stoppingConnection(logger, endpoint, null);
            }
        }
    }
}
