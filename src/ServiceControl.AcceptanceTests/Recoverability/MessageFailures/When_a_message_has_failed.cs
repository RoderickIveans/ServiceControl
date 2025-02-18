﻿using NServiceBus;

namespace ServiceControl.AcceptanceTests.Recoverability.MessageFailures
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using CompositeViews.Messages;
    using Contracts.Operations;
    using EventLog;
    using Infrastructure;
    using Infrastructure.SignalR;
    using Microsoft.AspNet.SignalR.Client;
    using Microsoft.AspNet.SignalR.Client.Transports;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTesting.Customization;
    using NServiceBus.Configuration.AdvancedExtensibility;
    using NServiceBus.Features;
    using NServiceBus.MessageInterfaces;
    using NServiceBus.Serialization;
    using NServiceBus.Settings;
    using NUnit.Framework;
    using ServiceControl.MessageFailures;
    using ServiceControl.MessageFailures.Api;
    using TestSupport;
    using TestSupport.EndpointTemplates;
    using JsonSerializer = Newtonsoft.Json.JsonSerializer;

    class When_a_message_has_failed : AcceptanceTest
    {
        [Test]
        public async Task Should_be_imported_and_accessible_via_the_rest_api()
        {
            FailedMessage failedMessage = null;

            var context = await Define<MyContext>()
                .WithEndpoint<Receiver>(b => b.When(bus => bus.SendLocal(new MyMessage())).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    var result = await this.TryGet<FailedMessage>("/api/errors/" + c.UniqueMessageId);
                    failedMessage = result;
                    return c.MessageId != null && result;
                })
                .Run();

            Assert.AreEqual(context.UniqueMessageId, failedMessage.UniqueMessageId);

            // The message Ids may contain a \ if they are from older versions.
            Assert.AreEqual(context.MessageId, failedMessage.ProcessingAttempts.Last().MessageId,
                "The returned message should match the processed one");
            Assert.AreEqual(FailedMessageStatus.Unresolved, failedMessage.Status, "Status should be set to unresolved");
            Assert.AreEqual(1, failedMessage.ProcessingAttempts.Count(), "Failed count should be 1");
            Assert.AreEqual("Simulated exception", failedMessage.ProcessingAttempts.Single().FailureDetails.Exception.Message,
                "Exception message should be captured");
        }

        [Theory]
        [TestCase(false)]
        [TestCase(true)] // creates body above 85000 bytes to make sure it is ingested into the body storage
        public async Task Should_be_imported_and_body_via_the_rest_api(bool largeMessage)
        {
            HttpResponseMessage result = null;

            var myMessage = new MyMessage
            {
                Content = largeMessage ? new string('a', 86 * 1024) : "Small"
            };

            await Define<MyContext>()
                .WithEndpoint<Receiver>(b => b.When(bus => bus.SendLocal(myMessage)).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    result = await this.GetRaw("/api/messages/" + c.MessageId + "/body");
                    return result.IsSuccessStatusCode;
                })
                .Run();

            Assert.AreEqual($"{{\"Content\":\"{myMessage.Content}\"}}", await result.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Should_be_imported_with_custom_serialization_and_body_via_the_rest_api()
        {
            HttpResponseMessage result = null;

            var myMessage = new MyMessage
            {
                Content = new string('a', 86 * 1024)
            };

            await Define<MyContext>()
                .WithEndpoint<ReceiverWithCustomSerializer>(b => b.When(bus => bus.SendLocal(myMessage)).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    result = await this.GetRaw("/api/messages/" + c.MessageId + "/body");
                    return result.IsSuccessStatusCode;
                })
                .Run();

            var content = await result.Content.ReadAsStringAsync();
            Assert.IsTrue(content.Contains($"<Content>{myMessage.Content}</Content>"));
        }

        [Test]
        public async Task Should_be_listed_in_the_error_list()
        {
            FailedMessageView failure = null;

            var context = await Define<MyContext>()
                .WithEndpoint<Receiver>(b => b.When(bus => bus.SendLocal(new MyMessage())).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    var result = await this.TryGetSingle<FailedMessageView>("/api/errors", r => r.MessageId == c.MessageId);
                    failure = result;
                    return result;
                })
                .Run();

            // The message Ids may contain a \ if they are from older versions.
            Assert.AreEqual(context.MessageId, failure.MessageId.Replace(@"\", "-"), "The returned message should match the processed one");
            Assert.AreEqual(FailedMessageStatus.Unresolved, failure.Status, "Status of new messages should be failed");
            Assert.AreEqual(1, failure.NumberOfProcessingAttempts, "One attempt should be stored");
        }

        [Test]
        public async Task Should_be_listed_in_the_messages_list()
        {
            var failure = new MessagesView();

            var context = await Define<MyContext>()
                .WithEndpoint<Receiver>(b => b.When(bus => bus.SendLocal(new MyMessage())).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    var result = await this.TryGetSingle<MessagesView>("/api/messages", m => m.MessageId == c.MessageId);
                    failure = result;
                    return result;
                })
                .Run(TimeSpan.FromMinutes(2));

            Assert.AreEqual(context.UniqueMessageId, failure.Id, "The unique id should be returned");

            Assert.AreEqual(MessageStatus.Failed, failure.Status, "Status of new messages should be failed");
            Assert.AreEqual(context.EndpointNameOfReceivingEndpoint, failure.SendingEndpoint.Name);
            Assert.AreEqual(context.EndpointNameOfReceivingEndpoint, failure.ReceivingEndpoint.Name);
        }

        [Test]
        public async Task Should_add_an_event_log_item()
        {
            EventLogItem entry = null;

            var context = await Define<MyContext>()
                .WithEndpoint<Receiver>(b => b.When(bus => bus.SendLocal(new MyMessage())).DoNotFailOnErrorMessages())
                .Done(async c =>
                {
                    var result = await this.TryGetSingle<EventLogItem>("/api/eventlogitems/", e => e.RelatedTo.Any(r => r.Contains(c.UniqueMessageId)) && e.EventType == nameof(Contracts.MessageFailures.MessageFailed));
                    entry = result;
                    return result;
                })
                .Run();

            Assert.AreEqual(Severity.Error, entry.Severity, "Failures should be treated as errors");
            Assert.IsTrue(entry.Description.Contains("exception"), "For failed messages, the description should contain the exception information");
            Assert.IsTrue(entry.RelatedTo.Any(item => item == "/message/" + context.UniqueMessageId), "Should contain the api url to retrieve additional details about the failed message");
            Assert.IsTrue(entry.RelatedTo.Any(item => item == "/endpoint/" + context.EndpointNameOfReceivingEndpoint), "Should contain the api url to retrieve additional details about the endpoint where the message failed");
        }

        [Test]
        public async Task Should_raise_a_signalr_event()
        {
            var context = await Define<MyContext>(ctx => { ctx.Handler = () => Handler; })
                .WithEndpoint<Receiver>(b => b.DoNotFailOnErrorMessages())
                .WithEndpoint<EndpointThatUsesSignalR>()
                .Done(c => c.SignalrEventReceived)
                .Run();

            Assert.IsNotNull(context.SignalrData);
        }

        [Test]
        public async Task Should_be_able_to_search_queueaddresses()
        {
            var searchResults = new List<QueueAddress>();

            await Define<QueueSearchContext>()
                .WithEndpoint<FailingEndpoint>(b =>
                {
                    b.DoNotFailOnErrorMessages();
                    b.When(bus => bus.SendLocal(new MyMessage()));
                    b.When(async c =>
                    {
                        if (c.FailedMessageCount >= 1)
                        {
                            var result = await this.TryGetMany<QueueAddress>("/api/errors/queues/addresses?search=failing");
                            searchResults = result;
                            return result;
                        }

                        return false;
                    }, (session, ctx) => Task.FromResult(0));
                })
                .Done(c => searchResults.Count == 1)
                .Run();

            Assert.AreEqual(1, searchResults.Count, "Result count did not match");
        }

        [Test]
        public async Task Should_only_return_queueaddresses_that_startswith_search()
        {
            var searchResults = new List<QueueAddress>();
            const string searchTerm = "another";
            const string searchEndpointName = "AnotherFailingEndpoint";

            await Define<QueueSearchContext>()
                .WithEndpoint<FailingEndpoint>(b =>
                {
                    b.DoNotFailOnErrorMessages();
                    b.When(bus => bus.SendLocal(new MyMessage()));
                })
                .WithEndpoint<FailingEndpoint>(b =>
                {
                    b.DoNotFailOnErrorMessages();
                    b.CustomConfig(configuration => configuration.GetSettings().Set("NServiceBus.Routing.EndpointName", searchEndpointName));
                    b.When(bus => bus.SendLocal(new MyMessage()));
                })
                .WithEndpoint<FailingEndpoint>(b =>
                {
                    b.DoNotFailOnErrorMessages();
                    b.CustomConfig(configuration => configuration.GetSettings().Set("NServiceBus.Routing.EndpointName", "YetAnotherEndpoint"));
                    b.When(bus => bus.SendLocal(new MyMessage()));
                }).Done(async c =>
                {
                    if (c.FailedMessageCount < 3)
                    {
                        return false;
                    }

                    var result = await this.TryGetMany<QueueAddress>($"/api/errors/queues/addresses/search/{searchTerm}");
                    searchResults = result;
                    return result;
                })
                .Run();


            Assert.AreEqual(1, searchResults.Count, "Result count did not match");
            Assert.IsTrue(searchResults[0].PhysicalAddress.StartsWith(searchEndpointName, StringComparison.InvariantCultureIgnoreCase));
        }

        public class EndpointThatUsesSignalR : EndpointConfigurationBuilder
        {
            public EndpointThatUsesSignalR()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    var routing = c.ConfigureTransport().Routing();
                    routing.RouteToEndpoint(typeof(MyMessage), typeof(Receiver));
                });
            }

            class SignalRStarterFeature : Feature
            {
                public SignalRStarterFeature()
                {
                    EnableByDefault();
                }

                protected override void Setup(FeatureConfigurationContext context)
                {
                    context.Container.ConfigureComponent<SignalRStarter>(DependencyLifecycle.SingleInstance);
                    context.RegisterStartupTask(b => b.Build<SignalRStarter>());
                }

                class SignalRStarter : FeatureStartupTask
                {
                    public SignalRStarter(MyContext context)
                    {
                        this.context = context;
                        connection = new Connection("http://localhost/api/messagestream")
                        {
                            JsonSerializer = JsonSerializer.Create(SerializationSettingsFactoryForSignalR.CreateDefault())
                        };
                    }

                    protected override Task OnStart(IMessageSession session)
                    {
                        connection.Received += ConnectionOnReceived;
                        connection.Start(new ServerSentEventsTransport(new SignalRHttpClient(context.Handler()))).GetAwaiter().GetResult();

                        return session.Send(new MyMessage());
                    }

                    protected override Task OnStop(IMessageSession session)
                    {
                        connection.Stop();
                        return Task.FromResult(0);
                    }

                    void ConnectionOnReceived(string s)
                    {
                        if (s.IndexOf("\"MessageFailuresUpdated\"") > 0)
                        {
                            context.SignalrData = s;
                            context.SignalrEventReceived = true;
                        }
                    }

                    MyContext context;
                    Connection connection;
                }
            }
        }

        public class Receiver : EndpointConfigurationBuilder
        {
            public Receiver()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.NoRetries();
                    c.ReportSuccessfulRetriesToServiceControl();
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyContext Context { get; set; }

                public ReadOnlySettings Settings { get; set; }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    Context.EndpointNameOfReceivingEndpoint = Settings.EndpointName();
                    Context.LocalAddress = Settings.LocalAddress();
                    Context.MessageId = context.MessageId.Replace(@"\", "-");
                    throw new Exception("Simulated exception");
                }
            }
        }

        public class ReceiverWithCustomSerializer : EndpointConfigurationBuilder
        {
            public ReceiverWithCustomSerializer()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.NoRetries();
                    c.ReportSuccessfulRetriesToServiceControl();
                    c.UseSerialization<MySuperSerializer>();
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyContext Context { get; set; }

                public ReadOnlySettings Settings { get; set; }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    Context.EndpointNameOfReceivingEndpoint = Settings.EndpointName();
                    Context.LocalAddress = Settings.LocalAddress();
                    Context.MessageId = context.MessageId.Replace(@"\", "-");
                    throw new Exception("Simulated exception");
                }
            }
        }
        class MySuperSerializer : SerializationDefinition
        {
            public override Func<IMessageMapper, IMessageSerializer> Configure(ReadOnlySettings settings)
            {
                return mapper => new MyCustomSerializer();
            }
        }

        public class MyCustomSerializer : IMessageSerializer
        {
            public void Serialize(object message, Stream stream)
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(MyMessage));

                serializer.Serialize(stream, message);
            }

            public object[] Deserialize(Stream stream, IList<Type> messageTypes = null)
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(MyMessage));

                stream.Position = 0;
                var msg = serializer.Deserialize(stream);

                return new[]
                {
                    msg
                };
            }

            public string ContentType => "MyCustomSerializer";
        }

        public class FailingEndpoint : EndpointConfigurationBuilder
        {
            public FailingEndpoint()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    var recoverability = c.Recoverability();
                    recoverability.Immediate(x => x.NumberOfRetries(0));
                    recoverability.Delayed(x => x.NumberOfRetries(0));
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public QueueSearchContext Context { get; set; }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    lock (lockObj)
                    {
                        Context.FailedMessageCount++;
                    }

                    throw new Exception("Simulated exception");
                }

                static object lockObj = new object();
            }
        }


        public class MyContext : ScenarioContext
        {
            public string MessageId { get; set; }
            public string EndpointNameOfReceivingEndpoint { get; set; }

            public string UniqueMessageId => DeterministicGuid.MakeId(MessageId, EndpointNameOfReceivingEndpoint).ToString();

            public Func<HttpMessageHandler> Handler { get; set; }
            public bool SignalrEventReceived { get; set; }
            public string SignalrData { get; set; }
            public string LocalAddress { get; set; }
        }

        public class QueueSearchContext : ScenarioContext
        {
            public int FailedMessageCount { get; set; }
        }
    }
}

public class MyMessage : ICommand
{
    public string Content { get; set; }
}