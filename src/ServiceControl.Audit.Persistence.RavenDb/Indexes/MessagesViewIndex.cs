namespace ServiceControl.Audit.Persistence.RavenDb.Indexes
{
    using System;
    using System.Linq;
    using Lucene.Net.Analysis.Standard;
    using Raven.Abstractions.Indexing;
    using Raven.Client.Indexes;
    using ServiceControl.Audit.Auditing;
    using ServiceControl.Audit.Monitoring;

    public class MessagesViewIndex : AbstractIndexCreationTask<ProcessedMessage, MessagesViewIndex.SortAndFilterOptions>
    {
        public MessagesViewIndex()
        {
            Map = messages => from message in messages
                              select new SortAndFilterOptions
                              {
                                  MessageId = (string)message.MessageMetadata["MessageId"],
                                  MessageType = (string)message.MessageMetadata["MessageType"],
                                  IsSystemMessage = (bool)message.MessageMetadata["IsSystemMessage"],
                                  Status = (bool)message.MessageMetadata["IsRetried"] ? MessageStatus.ResolvedSuccessfully : MessageStatus.Successful,
                                  TimeSent = (DateTime)message.MessageMetadata["TimeSent"],
                                  ProcessedAt = message.ProcessedAt,
                                  ReceivingEndpointName = ((EndpointDetails)message.MessageMetadata["ReceivingEndpoint"]).Name,
                                  CriticalTime = (TimeSpan?)message.MessageMetadata["CriticalTime"],
                                  ProcessingTime = (TimeSpan?)message.MessageMetadata["ProcessingTime"],
                                  DeliveryTime = (TimeSpan?)message.MessageMetadata["DeliveryTime"],
                                  Query = message.MessageMetadata.Select(_ => _.Value.ToString()).Union(new[] { string.Join(" ", message.Headers.Select(x => x.Value)) }).ToArray(),
                                  ConversationId = (string)message.MessageMetadata["ConversationId"]
                              };

            Index(x => x.Query, FieldIndexing.Analyzed);

            Analyze(x => x.Query, typeof(StandardAnalyzer).AssemblyQualifiedName);

            DisableInMemoryIndexing = true;
        }

        public class SortAndFilterOptions
        {
            public string MessageId { get; set; }
            public string MessageType { get; set; }
            public bool IsSystemMessage { get; set; }
            public MessageStatus Status { get; set; }
            public DateTime ProcessedAt { get; set; }
            public string ReceivingEndpointName { get; set; }
            public TimeSpan? CriticalTime { get; set; }
            public TimeSpan? ProcessingTime { get; set; }
            public TimeSpan? DeliveryTime { get; set; }
            public string ConversationId { get; set; }
            public string[] Query { get; set; }
            public DateTime TimeSent { get; set; }
        }
    }
}