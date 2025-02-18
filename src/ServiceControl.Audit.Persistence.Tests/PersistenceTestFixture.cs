﻿namespace ServiceControl.Audit.Persistence.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Auditing.BodyStorage;
    using NUnit.Framework;
    using UnitOfWork;

    [TestFixture]
    class PersistenceTestFixture
    {
        public Action<PersistenceSettings> SetSettings = _ => { };

        [SetUp]
        public virtual Task Setup()
        {
            configuration = new PersistenceTestsConfiguration();

            return configuration.Configure(SetSettings);
        }

        [TearDown]
        public virtual Task Cleanup()
        {
            return configuration?.Cleanup();
        }

        protected string GetManifestPath()
        {
            var currentFolder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (currentFolder != null)
            {
                var file = currentFolder.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly)
                    .SingleOrDefault();

                if (file != null)
                {
                    return Path.Combine(file.Directory.FullName, $"ServiceControl.Audit.Persistence.{PersisterName}", "persistence.manifest");
                }

                currentFolder = currentFolder.Parent;
            }

            throw new Exception($"Cannot find manifest folder for {PersisterName}");
        }

        protected string PersisterName => configuration.Name;

        protected IAuditDataStore DataStore => configuration.AuditDataStore;

        protected IFailedAuditStorage FailedAuditStorage => configuration.FailedAuditStorage;

        protected IBodyStorage BodyStorage => configuration.BodyStorage;

        protected IAuditIngestionUnitOfWorkFactory AuditIngestionUnitOfWorkFactory =>
            configuration.AuditIngestionUnitOfWorkFactory;

        protected IAuditIngestionUnitOfWork StartAuditUnitOfWork(int batchSize) =>
            AuditIngestionUnitOfWorkFactory.StartNew(batchSize);

        protected PersistenceTestsConfiguration configuration;
    }
}