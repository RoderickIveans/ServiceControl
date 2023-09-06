namespace ServiceControl.Persistence
{
    using System;
    using System.Linq;
    using Lucene.Net.Analysis.Standard;
    using Raven.Abstractions.Indexing;
    using Raven.Client.Indexes;
    using ServiceControl.MessageAuditing;
    using ServiceControl.MessageFailures;
    using ServiceControl.Operations;

    class MessagesViewIndex : AbstractMultiMapIndexCreationTask<MessagesViewIndex.SortAndFilterOptions>
    {
        public MessagesViewIndex()
        {
            AddMap<ProcessedMessage>(messages => from message in messages
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
                                                 });

            AddMap<FailedMessage>(messages => from message in messages
                                              let last = message.ProcessingAttempts.Last()
                                              select new SortAndFilterOptions
                                              {
                                                  MessageId = last.MessageId,
                                                  MessageType = (string)last.MessageMetadata["MessageType"],
                                                  IsSystemMessage = (bool)last.MessageMetadata["IsSystemMessage"],
                                                  Status = message.Status == FailedMessageStatus.Archived
                                                      ? MessageStatus.ArchivedFailure
                                                        : message.Status == FailedMessageStatus.Resolved
                                                            ? MessageStatus.ResolvedSuccessfully
                                                              : message.ProcessingAttempts.Count == 1
                                                                  ? MessageStatus.Failed
                                                                  : MessageStatus.RepeatedFailure,
                                                  TimeSent = (DateTime)last.MessageMetadata["TimeSent"],
                                                  ProcessedAt = last.AttemptedAt,
                                                  ReceivingEndpointName = ((EndpointDetails)last.MessageMetadata["ReceivingEndpoint"]).Name,
                                                  CriticalTime = null,
                                                  ProcessingTime = null,
                                                  DeliveryTime = null,
                                                  Query = last.MessageMetadata.Select(_ => _.Value.ToString()).Union(new[] { string.Join(" ", last.Headers.Select(x => x.Value)) }).ToArray(),
                                                  ConversationId = (string)last.MessageMetadata["ConversationId"]
                                              });

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