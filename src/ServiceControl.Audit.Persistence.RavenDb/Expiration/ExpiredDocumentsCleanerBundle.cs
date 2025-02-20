﻿namespace ServiceControl.Audit.Persistence.RavenDB.Expiration
{
    using System;
    using System.ComponentModel.Composition;
    using System.Threading.Tasks;
    using NServiceBus.Logging;
    using Raven.Database;
    using Raven.Database.Plugins;
    using ServiceControl.Audit.Infrastructure;

    [InheritedExport(typeof(IStartupTask))]
    [ExportMetadata("Bundle", "customDocumentExpiration")]
    public class ExpiredDocumentsCleanerBundle : IStartupTask, IDisposable
    {
        public void Dispose()
        {
            lock (this)
            {
                if (timer == null)
                {
                    return;
                }

                var stopTask = timer.Stop();
                var delayTask = Task.Delay(TimeSpan.FromSeconds(30));
                var composite = Task.WhenAny(stopTask, delayTask);

                var finishedTask = composite.GetAwaiter().GetResult();
                if (finishedTask == delayTask)
                {
                    logger.Error("Cleanup process did not finish on time. Forcing shutdown.");
                }
                else
                {
                    logger.Info("Expired documents cleanup process stopped.");
                }

                timer = null;
            }
        }

        public void Execute(DocumentDatabase database)
        {
            var deleteFrequencyInSeconds = ExpirationProcessTimerInSeconds;

            if (deleteFrequencyInSeconds == 0)
            {
                return;
            }

            var due = TimeSpan.FromSeconds(deleteFrequencyInSeconds);

            var deletionBatchSize = ExpirationProcessBatchSize;

            var auditRetentionPeriod = RavenBootstrapper.Settings.AuditRetentionPeriod;
            logger.Info($"Running deletion of expired documents every {deleteFrequencyInSeconds} seconds");
            logger.Info($"Deletion batch size set to {deletionBatchSize}");
            logger.Info($"Retention period for audits and saga history is {auditRetentionPeriod}");

            timer = new AsyncTimer(
                token => ExpiredDocumentsCleaner.RunCleanup(deletionBatchSize, database, auditRetentionPeriod, token), due, due, e => { logger.Error("Error when trying to find expired documents", e); });
        }

        int ExpirationProcessTimerInSeconds
        {
            get
            {
                var expirationProcessTimerInSeconds = ExpirationProcessTimerInSecondsDefault;

                if (RavenBootstrapper.Settings.PersisterSpecificSettings.TryGetValue(RavenBootstrapper.ExpirationProcessTimerInSecondsKey, out var expirationProcessTimerInSecondsString))
                {
                    expirationProcessTimerInSeconds = int.Parse(expirationProcessTimerInSecondsString);
                }

                if (expirationProcessTimerInSeconds < 0)
                {
                    logger.Error($"ExpirationProcessTimerInSeconds cannot be negative. Defaulting to {ExpirationProcessTimerInSecondsDefault}");
                    return ExpirationProcessTimerInSecondsDefault;
                }

                if (expirationProcessTimerInSeconds > TimeSpan.FromHours(3).TotalSeconds)
                {
                    logger.Error($"ExpirationProcessTimerInSeconds cannot be larger than {TimeSpan.FromHours(3).TotalSeconds}. Defaulting to {ExpirationProcessTimerInSecondsDefault}");
                    return ExpirationProcessTimerInSecondsDefault;
                }

                return expirationProcessTimerInSeconds;
            }
        }

        public int ExpirationProcessBatchSize
        {
            get
            {
                var expirationProcessBatchSize = ExpirationProcessBatchSizeDefault;

                if (RavenBootstrapper.Settings.PersisterSpecificSettings.TryGetValue(RavenBootstrapper.ExpirationProcessBatchSizeKey, out var expirationProcessBatchSizeString))
                {
                    expirationProcessBatchSize = int.Parse(expirationProcessBatchSizeString);
                }
                if (expirationProcessBatchSize < 1)
                {
                    logger.Error($"ExpirationProcessBatchSize cannot be less than 1. Defaulting to {ExpirationProcessBatchSizeDefault}");
                    return ExpirationProcessBatchSizeDefault;
                }

                if (expirationProcessBatchSize < ExpirationProcessBatchSizeMinimum)
                {
                    logger.Error($"ExpirationProcessBatchSize cannot be less than {ExpirationProcessBatchSizeMinimum}. Defaulting to {ExpirationProcessBatchSizeDefault}");
                    return ExpirationProcessBatchSizeDefault;
                }

                return expirationProcessBatchSize;
            }
        }

        const int ExpirationProcessTimerInSecondsDefault = 600;
        const int ExpirationProcessBatchSizeDefault = 65512;
        const int ExpirationProcessBatchSizeMinimum = 10240;

        ILog logger = LogManager.GetLogger(typeof(ExpiredDocumentsCleanerBundle));
        AsyncTimer timer;
    }
}