namespace ServiceControl.MultiInstance.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using AcceptanceTesting;
    using Newtonsoft.Json;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTesting.Support;
    using NUnit.Framework;
    using ServiceBus.Management.Infrastructure.Settings;
    using ServiceControl.AcceptanceTesting.InfrastructureConfig;
    using TestSupport;

    [TestFixture]
    abstract class AcceptanceTest : NServiceBusAcceptanceTest, IAcceptanceTestInfrastructureProviderMultiInstance
    {
        protected AcceptanceTest()
        {
            ServicePointManager.DefaultConnectionLimit = int.MaxValue;
            ServicePointManager.MaxServicePoints = int.MaxValue;
            ServicePointManager.UseNagleAlgorithm = false; // Improvement for small tcp packets traffic, get buffered up to 1/2-second. If your storage communication is for small (less than ~1400 byte) payloads, this setting should help (especially when dealing with things like Azure Queues, which tend to have very small messages).
            ServicePointManager.Expect100Continue = false; // This ensures tcp ports are free up quicker by the OS, prevents starvation of ports
            ServicePointManager.SetTcpKeepAlive(true, 5000, 1000); // This is good for Azure because it reuses connections
        }

        protected static string ServiceControlInstanceName { get; } = Settings.DEFAULT_SERVICE_NAME;
        protected static string ServiceControlAuditInstanceName { get; } = Audit.Infrastructure.Settings.Settings.DEFAULT_SERVICE_NAME;

        public Dictionary<string, HttpClient> HttpClients => serviceControlRunnerBehavior.HttpClients;
        public JsonSerializerSettings SerializerSettings => serviceControlRunnerBehavior.SerializerSettings;
        public Dictionary<string, dynamic> SettingsPerInstance => serviceControlRunnerBehavior.SettingsPerInstance;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            Scenario.GetLoggerFactory = ctx => new StaticLoggerFactory(ctx);
        }

        [SetUp]
        public void Setup()
        {
            CustomEndpointConfiguration = c => { };
            CustomAuditEndpointConfiguration = c => { };
            CustomServiceControlSettings = s => { };
            CustomServiceControlAuditSettings = s => { };
#if !NETCOREAPP2_0
            ConfigurationManager.GetSection("X");
#endif

            var logfilesPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "logs");
            Directory.CreateDirectory(logfilesPath);
            var logFile = Path.Combine(logfilesPath, $"{TestContext.CurrentContext.Test.ID}-{TestContext.CurrentContext.Test.Name}.txt");
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            textWriterTraceListener = new TextWriterTraceListener(logFile);
            Trace.Listeners.Add(textWriterTraceListener);

            TransportIntegration = (ITransportIntegration)TestSuiteConstraints.Current.CreateTransportConfiguration();

            if (TransportIntegration.GetType() != typeof(ConfigureEndpointLearningTransport))
            {
                Assert.Inconclusive($"Multi instance tests are only run for the learning transport");
            }

            DataStoreConfiguration = TestSuiteConstraints.Current.CreateDataStoreConfiguration();

            if (DataStoreConfiguration.DataStoreTypeName != nameof(DataStoreType.RavenDB35))
            {
                Assert.Inconclusive($"Multi-instance tests are only with RavenDB 3.5 for the main instance and InMemory for the audit instance");
            }

            serviceControlRunnerBehavior = new ServiceControlComponentBehavior(TransportIntegration, DataStoreConfiguration, c => CustomEndpointConfiguration(c), c => CustomAuditEndpointConfiguration(c), s => CustomServiceControlSettings(s), s => CustomServiceControlAuditSettings(s));

            RemoveOtherTransportAssemblies(TransportIntegration.TypeName);
        }

        [TearDown]
        public void Teardown()
        {
            TransportIntegration = null;
            Trace.Flush();
            Trace.Close();
            Trace.Listeners.Remove(textWriterTraceListener);
        }

        static void RemoveOtherTransportAssemblies(string name)
        {
            var assembly = Type.GetType(name, true).Assembly;

            var currentDirectoryOfSelectedTransport = Path.GetDirectoryName(assembly.Location);
            var otherAssemblies = Directory.EnumerateFiles(currentDirectoryOfSelectedTransport, "ServiceControl.Transports.*.dll")
                .Where(transportAssembly => transportAssembly != assembly.Location);

            foreach (var transportAssembly in otherAssemblies)
            {
                File.Delete(transportAssembly);
            }
        }

        protected IScenarioWithEndpointBehavior<T> Define<T>() where T : ScenarioContext, new()
        {
            return Define<T>(c => { });
        }

        protected IScenarioWithEndpointBehavior<T> Define<T>(Action<T> contextInitializer) where T : ScenarioContext, new()
        {
            return Scenario.Define(contextInitializer)
                .WithComponent(serviceControlRunnerBehavior);
        }

        protected Action<EndpointConfiguration> CustomEndpointConfiguration = c => { };
        protected Action<EndpointConfiguration> CustomAuditEndpointConfiguration = c => { };
        protected Action<Settings> CustomServiceControlSettings = c => { };
        protected Action<Audit.Infrastructure.Settings.Settings> CustomServiceControlAuditSettings = c => { };
        protected ITransportIntegration TransportIntegration;
        protected DataStoreConfiguration DataStoreConfiguration;

        ServiceControlComponentBehavior serviceControlRunnerBehavior;
        TextWriterTraceListener textWriterTraceListener;
    }
}