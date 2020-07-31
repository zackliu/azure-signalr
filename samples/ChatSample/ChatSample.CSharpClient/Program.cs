using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace ChatSample.CSharpClient
{
    class Program
    {
        private static SemaphoreSlim _barrier;
        private static int GroupSendIntervalInMilliseconds = 50000;
        private static int ConnectionTtlSeconds = 200;
        private static int _total = 1;
        private static long _current = 0;
        private static long _count = 0;
        private static string _errorFile;
        static void Main(string[] args)
        {
            var url = "http://localhost:5050";
            _total = args.Length > 0 && int.TryParse(args[0], out var total) ? total : 1;
            var conc = args.Length > 1 && int.TryParse(args[1], out var concurrency) ? concurrency : 20;
            _errorFile = $"error_{DateTime.Now}.log";
            _barrier = new SemaphoreSlim(conc);

            while (Interlocked.Read(ref _count) < _total)
            {
                Console.WriteLine(Interlocked.Read(ref _count) + ": " + _total);
                var index = _current++;
                var currentUser = "user" + index;
                Interlocked.Increment(ref _count);
                _ = StartConnection(url, currentUser);
            }
            Console.ReadLine();
        }

        private static async Task StartConnection(string url, string currentUser)
        {
            await _barrier.WaitAsync();
            try
            {
                var proxy = await ConnectAsync(url + "/chat", currentUser, Console.Out);
                var token = new CancellationTokenSource(TimeSpan.FromSeconds(StaticRandom.Next(ConnectionTtlSeconds, ConnectionTtlSeconds + 200)));
                _ = StartSendLoop(proxy, currentUser, 10, token.Token);
            }
            finally
            {
                _barrier.Release();
            }
        }

        private static async Task StartSendLoop(HubConnection proxy, string currentUser, int length, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                var str = Stopwatch.GetTimestamp().ToString();
                var content = string.Join(':', Enumerable.Repeat<string>(str, length));
                try
                {
                    await proxy.InvokeAsync("GroupSend", currentUser, content);
                }catch(Exception e)
                {
                    Console.WriteLine($"{DateTime.Now}: User {currentUser}, {e.Message}");
                }
                await Task.Delay(GroupSendIntervalInMilliseconds);
            }

            await proxy.StopAsync();
        }

        private static async Task<HubConnection> ConnectAsync(string url, string user, TextWriter output, CancellationToken cancellationToken = default)
        {
            var startT = DateTime.Now;
            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                .AddMessagePackProtocol().Build();
            
            connection.On<string, string>("broadcastMessage", BroadcastMessage);
            connection.On<string, string>("echo", Echo);
            connection.Closed += async (e) =>
            {
                var elapsed = (DateTime.Now - startT).TotalSeconds;
                var log = $"time: {DateTime.Now}, connId: {connection.ConnectionId}, user: {user}, elapsed seconds: {elapsed}, error: {e.Message} \n";
                await File.AppendAllTextAsync(_errorFile, log);
                output.WriteLine(log);
                Interlocked.Decrement(ref _count);
                if (e != null)
                {
                    output.WriteLine(e);
                    // await DelayRandom(200, 1000);
                    // await StartAsyncWithRetry(connection, user, output, cancellationToken);
                }
                else
                {
                    Interlocked.Decrement(ref _count);
                }
            };

            await StartAsyncWithRetry(connection, user, output, cancellationToken);

            return connection;
        }

        private static async Task StartAsyncWithRetry(HubConnection connection, string user, TextWriter output, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await connection.StartAsync(cancellationToken);
                    output.WriteLine($"{user}[{connection.ConnectionId}] started");
                    return;
                }
                catch (Exception e)
                {
                    output.WriteLine($"Error starting: {e.Message}, retry...");
                    await DelayRandom(200, 1000);
                }
            }
        }

        /// <summary>
        /// Delay random milliseconds
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        private static Task DelayRandom(int min, int max)
        {
            return Task.Delay(StaticRandom.Next(min, max));
        }

        private static void BroadcastMessage(string name, string message)
        {
            Console.WriteLine($"{name}: {message}");
        }

        private static void Echo(string name, string message)
        {
            Console.WriteLine($"{name}: {message}");
        }

        private enum Mode
        {
            Broadcast,
            Echo,
        }
    }
}
