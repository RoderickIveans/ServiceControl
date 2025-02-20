﻿namespace ServiceControl.AcceptanceTests.Monitoring.CustomChecks
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Infrastructure.SignalR;
    using Microsoft.AspNet.SignalR.Client;
    using Microsoft.AspNet.SignalR.Client.Transports;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.CustomChecks;
    using NServiceBus.Features;
    using NUnit.Framework;
    using ServiceBus.Management.Infrastructure.Settings;
    using TestSupport.EndpointTemplates;
    using JsonSerializer = Newtonsoft.Json.JsonSerializer;

    [TestFixture]
    [RunOnAllDataStores]
    class When_reporting_custom_check_with_signalr : AcceptanceTest
    {
        [Test]
        public async Task Should_result_in_a_custom_check_failed_event()
        {
            var context = await Define<MyContext>(ctx => { ctx.Handler = () => Handler; })
                .WithEndpoint<EndpointWithCustomCheck>()
                .WithEndpoint<EndpointThatUsesSignalR>()
                .Done(c => c.SignalrEventReceived)
                .Run(TimeSpan.FromMinutes(2));

            Assert.True(context.SignalrData.IndexOf("\"severity\": \"error\",") > 0);
        }

        public class MyContext : ScenarioContext
        {
            public bool SignalrEventReceived { get; set; }
            public Func<HttpMessageHandler> Handler { get; set; }
            public string SignalrData { get; set; }
        }

        public class EndpointThatUsesSignalR : EndpointConfigurationBuilder
        {
            public EndpointThatUsesSignalR()
            {
                EndpointSetup<DefaultServer>(c => c.EnableFeature<EnableSignalR>());
            }

            class EnableSignalR : Feature
            {
                protected override void Setup(FeatureConfigurationContext context)
                {
                    context.Container.ConfigureComponent<SignalrStarter>(DependencyLifecycle.SingleInstance);
                    context.RegisterStartupTask(b => b.Build<SignalrStarter>());
                }
            }

            class SignalrStarter : FeatureStartupTask
            {
                public SignalrStarter(MyContext context)
                {
                    this.context = context;
                    connection = new Connection("http://localhost/api/messagestream")
                    {
                        JsonSerializer = JsonSerializer.Create(SerializationSettingsFactoryForSignalR.CreateDefault())
                    };
                }

                void ConnectionOnReceived(string s)
                {
                    if (s.IndexOf("\"EventLogItemAdded\"") > 0)
                    {
                        if (s.IndexOf("EventLogItem/CustomChecks/CustomCheckFailed") > 0)
                        {
                            context.SignalrData = s;
                            context.SignalrEventReceived = true;
                        }
                    }
                }

                protected override Task OnStart(IMessageSession session)
                {
                    connection.Received += ConnectionOnReceived;

                    return connection.Start(new ServerSentEventsTransport(new SignalRHttpClient(context.Handler())));
                }

                protected override Task OnStop(IMessageSession session)
                {
                    connection.Stop();
                    return Task.FromResult(0);
                }

                readonly MyContext context;
                Connection connection;
            }
        }

        class EndpointWithCustomCheck : EndpointConfigurationBuilder
        {
            public EndpointWithCustomCheck()
            {
                EndpointSetup<DefaultServer>(c => { c.ReportCustomChecksTo(Settings.DEFAULT_SERVICE_NAME, TimeSpan.FromSeconds(1)); });
            }

            public class EventuallyFailingCustomCheck : CustomCheck
            {
                public EventuallyFailingCustomCheck()
                    : base("EventuallyFailingCustomCheck", "Testing", TimeSpan.FromSeconds(1))
                {
                }

                public override Task<CheckResult> PerformCheck()
                {
#pragma warning disable IDE0047 // Remove unnecessary parentheses
                    if ((Interlocked.Increment(ref counter) / 5) % 2 == 1)
#pragma warning restore IDE0047 // Remove unnecessary parentheses
                    {
                        return Task.FromResult(CheckResult.Failed("fail!"));
                    }

                    return Task.FromResult(CheckResult.Pass);
                }

                static int counter;
            }
        }
    }
}