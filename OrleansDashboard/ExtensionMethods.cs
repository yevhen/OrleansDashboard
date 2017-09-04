﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Orleans;
using Orleans.Providers;
using Orleans.Runtime.Configuration;

namespace OrleansDashboard
{
    public static class ExtensionMethods
    {
        public static async Task WithTimeout(this Task task, TimeSpan timeout)
        {
            var delay = Task.Delay(timeout);
            var any = await Task.WhenAny(delay, task);
            if (any != delay)
            {
                await task;
                return;
            }

            task.Ignore();
            throw new TimeoutException($"Task took more than specified {timeout}");
        }

        public static async Task ReturnFile(this IOwinContext context, string name, string contentType)
        {
            context.Response.ContentType = contentType;
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream($"OrleansDashboard.{name}"))
            using (var reader = new StreamReader(stream))
            {
                var content = await reader.ReadToEndAsync();
                await context.Response.WriteAsync(content);
            }
        }

        public static async Task ReturnBinaryFile(this IOwinContext context, string name, string contentType)
        {
            context.Response.ContentType = contentType;
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream($"OrleansDashboard.{name}"))
            using (var reader = new BinaryReader(stream))
            {
                var content = reader.ReadBytes((int)stream.Length);
                await context.Response.WriteAsync(content);
            }
        }

        public static Task ReturnJson(this IOwinContext context, object value)
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(
                JsonConvert.SerializeObject(value,
                new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                }));
        }

        public static Task ReturnUnauthorised(this IOwinContext context)
        {
            context.Response.StatusCode = 401;
            context.Response.ReasonPhrase = "Unauthorized";
            context.Response.Headers.Add("WWW-Authenticate", new string[] { "Basic realm=\"OrleansDashboard\"" });
            return Task.FromResult(0);
        }

        public static double SumZero<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector)
        {
            if (!source.Any()) return 0;
            return source.Sum(selector);
        }

        public static long SumZero<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector)
        {
            if (!source.Any()) return 0;
            return source.Sum(selector);
        }


        public static double AverageZero<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector)
        {
            if (!source.Any()) return 0;
            return source.Average(selector);
        }


        public static string ToSiloAddress(this string value)
        {
            var parts = value.Split(':');
            return $"{parts[0].Substring(1)}:{parts[1]}@{parts[2]}";
        }

        public static void RegisterDashboard(this GlobalConfiguration config, int port = 8080, string username = null, string password = null)
        {
            var settings = new Dictionary<string, string>
            {
                { "Port", port.ToString() }
            };
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                settings.Add("Username", username);
                settings.Add("Password", password);
            }
            config.RegisterBootstrapProvider<Dashboard>("Dashboard", settings);

            config.RegisterStatisticsProvider<StatsPublisher>("DashboardStats");
        }


        public static string ToSiloAddress(this IProviderRuntime providerRuntime)
        {
            var parts = providerRuntime.SiloIdentity.Substring(1).Split(':');
            return string.Format("{0}:{1}@{2}", parts[0], parts[1], parts[2]);
        }

        public static string ToPeriodString(this DateTime value)
        {
            return value.ToString("o").Split('.').First();
        }


    }
}
