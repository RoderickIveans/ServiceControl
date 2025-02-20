﻿namespace ServiceControl.Persistence.Tests
{
    using System.Threading.Tasks;
    using NUnit.Framework;
    using ServiceBus.Management.Infrastructure.Settings;
    using ServiceControl.Persistence.InMemory;
    using ServiceControl.Persistence.RavenDb;
    using ServiceControl.Persistence.SqlServer;

    class MagicStringTypeTests
    {
        [Test]
        public Task Verify_inmemory_persistence_type_string()
        {
            var typeNamespace = DataStoreConfig.InMemoryPersistenceTypeFullyQualifiedName.Split(',')[1].Trim();
            var typeFullName = DataStoreConfig.InMemoryPersistenceTypeFullyQualifiedName.Split(',')[0].Trim();
            var type = typeof(InMemoryPersistenceConfiguration);
            Assert.AreEqual(type.Namespace, typeNamespace);
            Assert.AreEqual(type.FullName, typeFullName);
            return Task.CompletedTask;
        }

        [Test]
        public Task Verify_sqlserver_persistence_type_string()
        {
            var typeNamespace = DataStoreConfig.SqlServerPersistenceTypeFullyQualifiedName.Split(',')[1].Trim();
            var typeFullName = DataStoreConfig.SqlServerPersistenceTypeFullyQualifiedName.Split(',')[0].Trim();
            var type = typeof(SqlDbPersistenceConfiguration);
            Assert.AreEqual(type.Namespace, typeNamespace);
            Assert.AreEqual(type.FullName, typeFullName);
            return Task.CompletedTask;
        }

        [Test]
        public Task Verify_ravendb_persistence_type_string()
        {
            var typeNamespace = DataStoreConfig.RavenDB35PersistenceTypeFullyQualifiedName.Split(',')[1].Trim();
            var typeFullName = DataStoreConfig.RavenDB35PersistenceTypeFullyQualifiedName.Split(',')[0].Trim();
            var type = typeof(RavenDbPersistenceConfiguration);
            Assert.AreEqual(type.Namespace, typeNamespace);
            Assert.AreEqual(type.FullName, typeFullName);
            return Task.CompletedTask;
        }
    }
}
