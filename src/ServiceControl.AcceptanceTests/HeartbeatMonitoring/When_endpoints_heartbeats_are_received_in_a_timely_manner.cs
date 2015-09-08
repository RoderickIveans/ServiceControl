﻿namespace ServiceBus.Management.AcceptanceTests.HeartbeatMonitoring
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Contexts;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;
    using ServiceControl.CompositeViews.Endpoints;

    public class When_endpoints_heartbeats_are_received_in_a_timely_manner : AcceptanceTest
    {
        public class Endpoint1 : EndpointConfigurationBuilder
        {
            public Endpoint1()
            {
                EndpointSetup<DefaultServerWithoutAudit>();
            }
        }

        public class Endpoint2 : EndpointConfigurationBuilder
        {
            public Endpoint2()
            {
                EndpointSetup<DefaultServerWithoutAudit>();
            }
        }

        public class MyContext : ScenarioContext
        {
        }

        [Test]
        public void Should_be_reflected_as_active_endpoints_in_the_heartbeat_summary()
        {
            var context = new MyContext();

            HeartbeatSummary summary = null;

            Scenario.Define(context)
                .WithEndpoint<ManagementEndpoint>(c => c.AppConfig(PathToAppConfig))
                .WithEndpoint<Endpoint1>()
                .WithEndpoint<Endpoint2>()
                .Done(c =>
                {
                    List<EndpointsView> endpoints;
                    if (!TryGetMany("/api/endpoints", out endpoints))
                    {
                        return false;
                    }

                    Console.WriteLine("Found {0} endpoints", endpoints.Count);
                    Console.WriteLine(String.Join(Environment.NewLine, endpoints.Select(x => x.Name)));

                    var endpoint1s = endpoints.Where(x => x.Name.Contains("Endpoint1")).ToArray();

                    if (endpoint1s.Length != 1)
                    {
                        Console.WriteLine("Endpoint 1 not registered");
                        return false;
                    }
                    
                    var endpoint2s = endpoints.Where(x => x.Name.Contains("Endpoint2")).ToArray();

                    if (endpoint2s.Length != 1)
                    {
                        Console.WriteLine("Endpoint 2 not registered");
                        return false;
                    }

                    if (endpoints.Count != 2)
                    {
                        Console.WriteLine("There should be two endpoints");
                        return false;
                    }

                    if (endpoints.Except(endpoint1s).Except(endpoint2s).Any())
                    {
                        Console.WriteLine("Endpoints other than 1 and 2 detected");
                        return false;
                    }

                    HeartbeatSummary local;
                    if (TryGet("/api/heartbeats/stats", out local))
                    {
                        Console.WriteLine("Stats: Active({0}) Failing({1})", local.Active, local.Failing);
                        summary = local;
                        return true;
                    }
                    Console.WriteLine("NOT DONE");
                    Thread.Sleep(500);
                    return false;
                })
                .Run(TimeSpan.FromMinutes(2));

            Assert.AreEqual(0, summary.Failing);
            Assert.AreEqual(2, summary.Active);
        }

        public class HeartbeatSummary
        {
            public int Active { get; set; }
            public int Failing { get; set; }
        }
    }
}