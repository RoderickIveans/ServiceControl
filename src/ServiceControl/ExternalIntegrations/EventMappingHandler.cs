﻿namespace ServiceControl.ExternalIntegrations
{
    using System.Collections.Generic;
    using System.Linq;
    using NServiceBus;
    using NServiceBus.Logging;
    using NServiceBus.Transports;
    using Raven.Client;

    public class EventMappingHandler : IHandleMessages<IEvent>
    {
        public ISendMessages MessageSender { get; set; }
        public IDocumentSession Session { get; set; }
        public IEnumerable<IEventPublisher> EventPublishers { get; set; } 

        public void Handle(IEvent message)
        {
            var references = EventPublishers
                .Where(p => p.Handles(message))
                .Select(p => p.CreateReference(message));

            foreach (var reference in references)
            {
                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugFormat("Storing dispatch request.");
                }
                var dispatchRequest = new ExternalIntegrationDispatchRequest()
                {
                    Reference = reference
                };
                Session.Store(dispatchRequest);    
            }
        }

        static readonly ILog Logger = LogManager.GetLogger(typeof(EventMappingHandler));
    }
}