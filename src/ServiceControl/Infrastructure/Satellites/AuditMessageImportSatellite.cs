﻿namespace ServiceBus.Management.Infrastructure.Satellites
{
    using NServiceBus;
    using NServiceBus.Logging;
    using NServiceBus.Satellites;
    using ServiceControl;
    using ServiceControl.Contracts.Operations;
    using Settings;

    public class AuditMessageImportSatellite : ISatellite
    {
        public IBus Bus { get; set; }

        public bool Handle(TransportMessage message)
        {
            Bus.InMemory.Raise<AuditMessageReceived>(m =>
            {
                m.AuditMessageId = message.UniqueId();
                m.PhysicalMessage = new PhysicalMessage(message);
            });
            return true;
        }

        public void Start()
        {
            Logger.InfoFormat("Audit import is now started, feeding audit messages from: {0}", InputAddress);
        }

        public void Stop()
        {
        }

        public Address InputAddress
        {
            get { return Settings.AuditQueue; }
        }

        public bool Disabled
        {
            get { return InputAddress == Address.Undefined; }
        }

      

        static readonly ILog Logger = LogManager.GetLogger(typeof(AuditMessageImportSatellite));
    }
}