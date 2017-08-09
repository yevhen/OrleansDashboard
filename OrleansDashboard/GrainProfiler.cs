﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Providers;
using Orleans.Runtime;

namespace OrleansDashboard
{
    public class GrainProfiler : IDisposable
    {
        public TaskScheduler TaskScheduler { get; private set; }
        public IProviderRuntime ProviderRuntime { get; private set; }
        object sync = new object();
        string siloAddress;
        public Logger Logger { get; private set; }

        public GrainProfiler(TaskScheduler taskScheduler, IProviderRuntime providerRuntime)
        {
            this.TaskScheduler = taskScheduler;
            this.ProviderRuntime = providerRuntime;
            this.Logger = this.ProviderRuntime.GetLogger("GrainProfiler");

            // register interceptor, wrapping any previously set interceptor
            this.innerInterceptor = providerRuntime.GetInvokeInterceptor();
            providerRuntime.SetInvokeInterceptor(this.InvokeInterceptor);
            siloAddress = providerRuntime.SiloIdentity.ToSiloAddress();

            // register timer to report every second
            timer = new Timer(this.ProcessStats, providerRuntime, 1 * 1000, 1 * 1000);

        }

        Task<object> Dispatch(Func<Task<object>> func)
        {
            return Task.Factory.StartNew(func, CancellationToken.None, TaskCreationOptions.None, scheduler: this.TaskScheduler).Result;
        }


        // capture stats
        async Task<object> InvokeInterceptor(MethodInfo targetMethod, InvokeMethodRequest request, IGrain grain, IGrainMethodInvoker invoker)
        {
            var grainName = grain.GetType().FullName;
            var stopwatch = Stopwatch.StartNew();

            // invoke grain
            object result = null;
            var isException = false;

            try
            {
                if (this.innerInterceptor != null)
                {
                    result = await this.innerInterceptor(targetMethod, request, grain, invoker).ConfigureAwait(false);
                }
                else
                {
                    result = await invoker.Invoke(grain, request).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                isException = true;
                throw;
            }
            finally
            {

                try
                {
                    stopwatch.Stop();

                    var elapsedMs = (double)stopwatch.ElapsedTicks / TimeSpan.TicksPerMillisecond;

                    var key = string.Format("{0}.{1}", grainName, GetMethodName(targetMethod, request));

                    grainTrace.AddOrUpdate(key, _ =>
                    {
                        return new GrainTraceEntry
                        {
                            Count = 1,
                            ExceptionCount = (isException ? 1 : 0),
                            SiloAddress = siloAddress,
                            ElapsedTime = elapsedMs,
                            Grain = grainName ,
                            Method = GetMethodName(targetMethod, request),
                            Period = DateTime.UtcNow
                        };
                    },
                    (_, last) =>
                    {
                        last.Count += 1;
                        last.ElapsedTime += elapsedMs;
                        if (isException) last.ExceptionCount += 1;
                        return last;
                    });
                }
                catch (Exception ex)
                {
                    this.Logger.Error(100002, "error recording results for grain", ex);
                }
            }

            return result;
        }

        readonly ConcurrentDictionary<MethodInfo, bool> messageArgumentMethods =
             new ConcurrentDictionary<MethodInfo, bool>();

        string GetMethodName(MethodInfo targetMethod, InvokeMethodRequest request)
        {
            if (targetMethod == null)
                return "Unknown";

            bool IsMessageArgumentMethod(MethodInfo m) => m.GetCustomAttributes().Any(x => x.GetType().Name == "MessageArgumentAttribute");
            if (messageArgumentMethods.GetOrAdd(targetMethod, IsMessageArgumentMethod))
            {
                var arg = request.Arguments[0];
                return arg?.GetType().Name ?? "NULL";
            }

            return targetMethod.Name;
        }

        Timer timer = null;
        ConcurrentDictionary<string, GrainTraceEntry> grainTrace = new ConcurrentDictionary<string, GrainTraceEntry>();

        // publish stats to a grain
        void ProcessStats(object state)
        {
            var providerRuntime = state as IProviderRuntime;
            var dashboardGrain = providerRuntime.GrainFactory.GetGrain<IDashboardGrain>(0);

            // flush the dictionary
            GrainTraceEntry[] data;
            lock (sync)
            {
                data = this.grainTrace.Values.ToArray();
                this.grainTrace.Clear();
            }

            foreach (var item in data)
            {
                item.Grain = TypeFormatter.Parse(item.Grain);
            }

            try
            {
                Dispatch(async () =>
                {
                    await dashboardGrain.SubmitTracing(siloAddress, data).ConfigureAwait(false);
                    return null;
                }).Wait(30000);
            }
            catch (Exception ex)
            {
                this.Logger.Log(100001, Severity.Warning, "Exception thrown sending tracing to dashboard grain", new object[0], ex);
            }
            
        }

        public void Dispose()
        {
            if (null == timer) return;
            timer.Dispose();
        }

        InvokeInterceptor innerInterceptor = null;

    }
}
