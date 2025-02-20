namespace ServiceControl.Audit.Persistence.RavenDb.Indexes
{
    using System.Linq;
    using Auditing;
    using Raven.Client.Indexes;

    public class ExpiryProcessedMessageIndex : AbstractIndexCreationTask<ProcessedMessage>
    {
        public ExpiryProcessedMessageIndex()
        {
            Map = messages => from message in messages
                              select new
                              {
                                  ProcessedAt = message.ProcessedAt.Ticks
                              };

            DisableInMemoryIndexing = true;
        }
    }
}