﻿namespace ServiceControl.Audit.Persistence.RavenDb
{
    using System.Collections.Generic;
    using Raven.Client.Embedded;
    using ServiceControl.Audit.Persistence.RavenDB;

    public class RavenDbPersistenceConfiguration : IPersistenceConfiguration
    {
        public string Name => "RavenDB35";

        public IEnumerable<string> ConfigurationKeys => new string[]{
            RavenBootstrapper.DatabasePathKey,
            RavenBootstrapper.HostNameKey,
            RavenBootstrapper.DatabaseMaintenancePortKey,
            RavenBootstrapper.ExposeRavenDBKey,
            RavenBootstrapper.ExpirationProcessTimerInSecondsKey,
            RavenBootstrapper.ExpirationProcessBatchSizeKey,
            RavenBootstrapper.RunCleanupBundleKey,
            RavenBootstrapper.RunInMemoryKey
        };

        public IPersistence Create(PersistenceSettings settings)
        {
            var documentStore = new EmbeddableDocumentStore();
            RavenBootstrapper.Configure(documentStore, settings);

            var ravenStartup = new RavenStartup();

            foreach (var indexAssembly in RavenBootstrapper.IndexAssemblies)
            {
                ravenStartup.AddIndexAssembly(indexAssembly);
            }

            return new RavenDbPersistence(settings, documentStore, ravenStartup);
        }
    }
}
