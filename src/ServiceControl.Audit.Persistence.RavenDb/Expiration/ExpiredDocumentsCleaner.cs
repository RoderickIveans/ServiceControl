﻿namespace ServiceControl.Audit.Persistence.RavenDB.Expiration
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus.Logging;
    using Raven.Abstractions;
    using Raven.Database;
    using ServiceControl.Audit.Infrastructure;
    using ServiceControl.SagaAudit;

    class ExpiredDocumentsCleaner
    {
        public static Task<TimerJobExecutionResult> RunCleanup(int deletionBatchSize, DocumentDatabase database, TimeSpan auditRetentionPeriod, CancellationToken cancellationToken = default)
        {
            var threshold = SystemTime.UtcNow.Add(-auditRetentionPeriod);

            if (logger.IsDebugEnabled)
            {
                logger.Debug($"Trying to find expired ProcessedMessage, SagaHistory and KnownEndpoint documents to delete (with threshold {threshold.ToString(Default.DateTimeFormatsToWrite, CultureInfo.InvariantCulture)})");
            }
            AuditMessageCleaner.Clean(deletionBatchSize, database, threshold, cancellationToken);
            KnownEndpointsCleaner.Clean(deletionBatchSize, database, threshold, cancellationToken);
            SagaHistoryCleaner.Clean(deletionBatchSize, database, threshold, cancellationToken);

            return Task.FromResult(TimerJobExecutionResult.ScheduleNextExecution);
        }

        static ILog logger = LogManager.GetLogger(typeof(ExpiredDocumentsCleaner));
    }
}