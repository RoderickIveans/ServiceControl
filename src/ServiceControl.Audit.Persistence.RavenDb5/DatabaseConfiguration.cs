﻿namespace ServiceControl.Audit.Persistence.RavenDb
{
    using System;
    using Sparrow.Json;

    public class DatabaseConfiguration
    {
        public DatabaseConfiguration(
            string name,
            int expirationProcessTimerInSeconds,
            bool enableFullTextSearch,
            TimeSpan auditRetentionPeriod,
            int maxBodySizeToStore,
            ServerConfiguration serverConfiguration)
        {
            Name = name;
            ExpirationProcessTimerInSeconds = expirationProcessTimerInSeconds;
            EnableFullTextSearch = enableFullTextSearch;
            AuditRetentionPeriod = auditRetentionPeriod;
            MaxBodySizeToStore = maxBodySizeToStore;
            ServerConfiguration = serverConfiguration;
        }

        public string Name { get; }

        public int ExpirationProcessTimerInSeconds { get; }

        public bool EnableFullTextSearch { get; }

        public Func<string, BlittableJsonReaderObject, string> FindClrType { get; }

        public ServerConfiguration ServerConfiguration { get; }

        public TimeSpan AuditRetentionPeriod { get; }

        public int MaxBodySizeToStore { get; }
    }
}
