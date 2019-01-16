﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR.Common;
using Microsoft.Azure.SignalR.Protocol;

namespace Microsoft.Azure.SignalR
{
    abstract class ServiceConnectionContainerBase : IServiceConnectionContainer, IServiceConnectionManager
    {
        private static readonly int MaxReconnectBackOffInternalInMilliseconds = 1000;
        private static TimeSpan ReconnectInterval =>
            TimeSpan.FromMilliseconds(StaticRandom.Next(MaxReconnectBackOffInternalInMilliseconds));

        protected readonly IServiceConnectionFactory ServiceConnectionFactory;
        protected readonly IConnectionFactory ConnectionFactory;
        protected readonly List<IServiceConnection> ServiceConnections;
        protected readonly int Count;

        private volatile int _defaultConnectionRetry;

        protected ServiceConnectionContainerBase(IServiceConnectionFactory serviceConnectionFactory,
            IConnectionFactory connectionFactory,
            int count)
        {
            ServiceConnectionFactory = serviceConnectionFactory;
            ConnectionFactory = connectionFactory;
            ServiceConnections = new List<IServiceConnection>(count);
            Count = count;
        }

        public virtual Task InitializeAsync()
        {
            var connections = CreateFixedServiceConnection(Count);
            return Task.WhenAll(connections.Select(c => c.StartAsync()));
        }

        // For test purpose only
        internal virtual void Initialize(List<IServiceConnection> connections)
        {
            for (int i = 0; i < Count && i < connections.Count; i++)
            {
                ServiceConnections.Add(connections[i]);
            }
        }

        /// <summary>
        /// Get a connection in initialization and reconnection
        /// </summary>
        protected abstract IServiceConnection GetSingleServiceConnection();

        /// <summary>
        /// Get a connection for a specific service connection type
        /// </summary>
        protected virtual IServiceConnection GetSingleServiceConnection(ServerConnectionType type)
        {
            return ServiceConnectionFactory.Create(ConnectionFactory, this, type);
        }

        public IEnumerable<IServiceConnection> CreateServiceConnection(int count = 1)
        {
            if (count <= 0)
            {
                throw new ArgumentException($"{nameof(count)} must be greater than 0.");
            }

            for (int i = 0; i < count; i++)
            {
                yield return CreateServiceConnectionCore();
            }
        }

        /// <summary>
        /// Create an on-demand connection.
        /// </summary>
        /// <returns>An on-demand connection</returns>
        protected abstract IServiceConnection CreateServiceConnectionCore();

        public abstract void DisposeServiceConnection(IServiceConnection connection);

        public virtual ServiceConnectionStatus Status => throw new NotSupportedException();

        public Task WriteAsync(ServiceMessage serviceMessage)
        {
            return WriteToRandomAvailableConnection(serviceMessage);
        }

        public Task WriteAsync(string partitionKey, ServiceMessage serviceMessage)
        {
            // If we hit this check, it is a code bug.
            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new ArgumentNullException(nameof(partitionKey));
            }

            return WriteToPartitionedConnection(partitionKey, serviceMessage);
        }

        protected virtual Task WriteToPartitionedConnection(string partitionKey, ServiceMessage serviceMessage)
        {
            return WriteWithRetry(serviceMessage, partitionKey.GetHashCode(), Count);
        }

        protected virtual Task WriteToRandomAvailableConnection(ServiceMessage serviceMessage)
        {
            return WriteWithRetry(serviceMessage, StaticRandom.Next(-Count, Count), Count);
        }

        protected virtual async Task WriteWithRetry(ServiceMessage serviceMessage, int initial, int count)
        {
            // go through all the connections, it can be useful when one of the remote service instances is down
            var maxRetry = count;
            var retry = 0;
            var index = (initial & int.MaxValue) % count;
            var direction = initial > 0 ? 1 : count - 1;
            while (retry < maxRetry)
            {
                var connection = ServiceConnections[index];
                if (connection != null && connection.Status == ServiceConnectionStatus.Connected)
                {
                    try
                    {
                        // still possible the connection is not valid
                        await connection.WriteAsync(serviceMessage);
                        return;
                    }
                    catch (ServiceConnectionNotActiveException)
                    {
                        if (retry == maxRetry - 1)
                        {
                            throw;
                        }
                    }
                }

                retry++;
                index = (index + direction) % count;
            }

            throw new ServiceConnectionNotActiveException();
        }

        protected virtual async Task ReconnectWithDelayAsync(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentException($"{nameof(index)} must between 0 and {Count}");
            }

            await Task.Delay(GetRetryDelay(_defaultConnectionRetry));

            // Increase retry count after delay, then if a group of connections get disconnected simultaneously,
            // all of them will delay a similar range of time and reconnect. But if they get disconnected again (when SignalR service down), 
            // they will all delay for a much longer time.
            Interlocked.Increment(ref _defaultConnectionRetry);

            var connection = GetSingleServiceConnection();
            ServiceConnections[index] = connection;

            await connection.StartAsync();
            if (connection.Status == ServiceConnectionStatus.Connected)
            {
                Interlocked.Exchange(ref _defaultConnectionRetry, 0);
            }
        }

        private TimeSpan GetRetryDelay(int retryCount)
        {
            // retry count:   0, 1, 2, 3, 4,  5,  6,  ...
            // delay seconds: 1, 2, 4, 8, 16, 32, 60, ...
            if (retryCount > 5)
            {
                return TimeSpan.FromMinutes(1) + ReconnectInterval;
            }
            return TimeSpan.FromSeconds(1 << retryCount) + ReconnectInterval;
        }

        private IEnumerable<IServiceConnection> CreateFixedServiceConnection(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var connection = GetSingleServiceConnection();
                ServiceConnections.Add(connection);
                yield return connection;
            }
        }
    }
}
