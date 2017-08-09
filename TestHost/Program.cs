﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime.Configuration;
using Orleans.TestingHost;
using OrleansDashboard;
using TestGrains;

namespace TestHost
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Starting silos");
            Console.WriteLine("Dashboard will listen on http://localhost:8080/");

            // Deploy 3 silos
            var options = new TestClusterOptions(3);
            options.ClusterConfiguration.Globals.RegisterDashboard();
            var cluster = new TestCluster(options);
            cluster.Deploy();

            // generate some calls to a test grain
            GrainClient.Initialize(ClientConfiguration.LocalhostSilo());
            Console.WriteLine("All silos are up and running");

            var tokenSource = new CancellationTokenSource();
            var t = new Thread(() => CallGenerator(tokenSource).Wait());
            t.Start();

            Console.ReadLine();
            tokenSource.Cancel();
            try
            {
                t.Join(TimeSpan.FromSeconds(3));
            }
            catch
            { }
            cluster.StopAllSilos();
        }

        private static async Task CallGenerator(CancellationTokenSource tokenSource)
        {
            var a = GrainClient.GrainFactory.GetGrain<ITestMessageBasedGrain>(42);
            a.Receive("string").Wait();
            a.Receive(DateTime.UtcNow).Wait();

            var x = GrainClient.GrainFactory.GetGrain<ITestGenericGrain<string, int>>("test");
            x.TestT("string").Wait();
            x.TestU(1).Wait();
            x.TestTU("string", 1).Wait();

            var rand = new Random();
            while (!tokenSource.IsCancellationRequested)
            {
                var client = GrainClient.GrainFactory.GetGrain<ITestGrain>(rand.Next(500));
                await client.ExampleMethod1();
                try
                {
                    await client.ExampleMethod2();
                }
                catch
                { }

                // interceptors are currently broken for generic grains
                // https://github.com/dotnet/orleans/issues/2358
                //var genericClient = GrainClient.GrainFactory.GetGrain<IGenericGrain<string>>("foo");
                //genericClient.Echo("hello world").Wait();
            }
        }
    }
}