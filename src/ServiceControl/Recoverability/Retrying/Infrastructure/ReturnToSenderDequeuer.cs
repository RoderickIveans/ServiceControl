namespace ServiceControl.Recoverability
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Infrastructure.DomainEvents;
    using MessageFailures;
    using Microsoft.Extensions.Hosting;
    using NServiceBus;
    using NServiceBus.Logging;
    using NServiceBus.Raw;
    using NServiceBus.Routing;
    using NServiceBus.Transport;
    using Raven.Client;
    using ServiceBus.Management.Infrastructure.Settings;

    class ReturnToSenderDequeuer : IHostedService
    {
        public ReturnToSenderDequeuer(ReturnToSender returnToSender, IDocumentStore store, IDomainEvents domainEvents, RawEndpointFactory rawEndpointFactory, Settings settings)
        {
            InputAddress = settings.StagingQueue;
            this.returnToSender = returnToSender;
            errorQueue = settings.ErrorQueue;

            createEndpointConfiguration = () =>
            {
                var config = rawEndpointFactory.CreateReturnToSenderDequeuer(InputAddress, Handle);

                config.CustomErrorHandlingPolicy(faultManager);

                return config;
            };

            faultManager = new CaptureIfMessageSendingFails(store, domainEvents, IncrementCounterOrProlongTimer);
            timer = new Timer(state => StopInternal().GetAwaiter().GetResult());

        }

        public string InputAddress { get; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Stop();

        bool IsCounting => targetMessageCount.HasValue;

        async Task Handle(MessageContext message, IDispatchMessages sender)
        {
            if (Log.IsDebugEnabled)
            {
                var stagingId = message.Headers["ServiceControl.Retry.StagingId"];
                Log.DebugFormat("Handling message with id {0} and staging id {1} in input queue {2}", message.MessageId, stagingId, InputAddress);
            }

            if (shouldProcess(message))
            {
                await returnToSender.HandleMessage(message, sender, errorQueueTransportAddress).ConfigureAwait(false);
                IncrementCounterOrProlongTimer();
            }
            else
            {
                Log.WarnFormat("Rejecting message from staging queue as it's not part of a fully staged batch: {0}", message.MessageId);
            }
        }

        void IncrementCounterOrProlongTimer()
        {
            if (IsCounting)
            {
                CountMessageAndStopIfReachedTarget();
            }
            else
            {
                if (Log.IsDebugEnabled)
                {
                    Log.Debug("Resetting timer");
                }

                timer.Change(TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
            }
        }

        void CountMessageAndStopIfReachedTarget()
        {
            var currentMessageCount = Interlocked.Increment(ref actualMessageCount);
            if (Log.IsDebugEnabled)
            {
                Log.Debug($"Forwarding message {currentMessageCount} of {targetMessageCount}.");
            }

            if (currentMessageCount >= targetMessageCount.GetValueOrDefault())
            {
                if (Log.IsDebugEnabled)
                {
                    Log.DebugFormat("Target count reached. Shutting down forwarder");
                }

                // NOTE: This needs to run on a different thread or a deadlock will happen trying to shut down the receiver
                _ = Task.Run(StopInternal);
            }
        }

        public virtual async Task Run(string forwardingBatchId, Predicate<MessageContext> filter, int? expectedMessageCount, CancellationToken cancellationToken = default)
        {
            IReceivingRawEndpoint processor = null;
            CancellationTokenRegistration? registration = null;
            try
            {
                shouldProcess = filter;
                targetMessageCount = expectedMessageCount;
                actualMessageCount = 0;

                if (Log.IsDebugEnabled)
                {
                    Log.DebugFormat("Starting receiver");
                }

                var config = createEndpointConfiguration();
                syncEvent = new TaskCompletionSource<bool>();
                stopCompletionSource = new TaskCompletionSource<bool>();
                registration = cancellationToken.Register(() => _ = Task.Run(() => syncEvent.TrySetResult(true), CancellationToken.None));

                var startable = await RawEndpoint.Create(config).ConfigureAwait(false);

                errorQueueTransportAddress = GetErrorQueueTransportAddress(startable);

                processor = await startable.Start().ConfigureAwait(false);

                Log.Info($"Forwarder for batch {forwardingBatchId} started receiving messages from {processor.TransportAddress}.");

                if (!expectedMessageCount.HasValue)
                {
                    if (Log.IsDebugEnabled)
                    {
                        Log.Debug("Running in timeout mode. Starting timer.");
                    }

                    timer.Change(TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
                }
            }
            finally
            {
                if (Log.IsDebugEnabled)
                {
                    Log.DebugFormat($"Waiting for forwarder for batch {forwardingBatchId} to finish.");
                }

                await syncEvent.Task.ConfigureAwait(false);
                registration?.Dispose();
                if (processor != null)
                {
                    await processor.Stop().ConfigureAwait(false);
                }

                Log.Info($"Forwarder for batch {forwardingBatchId} finished forwarding all messages.");

                await Task.Run(() => stopCompletionSource.TrySetResult(true), CancellationToken.None).ConfigureAwait(false);
            }

            if (endedPrematurely || cancellationToken.IsCancellationRequested)
            {
                throw new Exception("We are in the process of shutting down. Safe to ignore.");
            }
        }

        string GetErrorQueueTransportAddress(IStartableRawEndpoint startable)
        {
            var transportInfra = startable.Settings.Get<TransportInfrastructure>();
            var localInstance = transportInfra.BindToLocalEndpoint(new EndpointInstance(errorQueue));
            return transportInfra.ToTransportAddress(LogicalAddress.CreateRemoteAddress(localInstance));
        }

        public Task Stop()
        {
            timer.Dispose();
            endedPrematurely = true;
            return StopInternal();
        }

        async Task StopInternal()
        {
            if (Log.IsDebugEnabled)
            {
                Log.Debug("Completing forwarding.");
            }

            await Task.Run(() => syncEvent?.TrySetResult(true)).ConfigureAwait(false);
            await (stopCompletionSource?.Task ?? (Task)Task.FromResult(0)).ConfigureAwait(false);
            if (Log.IsDebugEnabled)
            {
                Log.Debug("Forwarding completed.");
            }
        }

        Timer timer;
        TaskCompletionSource<bool> syncEvent;
        TaskCompletionSource<bool> stopCompletionSource;
        bool endedPrematurely;
        int? targetMessageCount;
        int actualMessageCount;
        Predicate<MessageContext> shouldProcess;
        CaptureIfMessageSendingFails faultManager;
        Func<RawEndpointConfiguration> createEndpointConfiguration;
        ReturnToSender returnToSender;
        readonly string errorQueue;
        string errorQueueTransportAddress;

        static readonly ILog Log = LogManager.GetLogger(typeof(ReturnToSenderDequeuer));

        class CaptureIfMessageSendingFails : IErrorHandlingPolicy
        {
            public CaptureIfMessageSendingFails(IDocumentStore store, IDomainEvents domainEvents, Action executeOnFailure)
            {
                this.store = store;
                this.executeOnFailure = executeOnFailure;
                this.domainEvents = domainEvents;
            }

            public async Task<ErrorHandleResult> OnError(IErrorHandlingPolicyContext handlingContext, IDispatchMessages dispatcher)
            {
                try
                {
                    var message = handlingContext.Error.Message;
                    var destination = message.Headers["ServiceControl.TargetEndpointAddress"];
                    var messageUniqueId = message.Headers["ServiceControl.Retry.UniqueMessageId"];
                    Log.Warn($"Failed to send '{messageUniqueId}' message to '{destination}' for retry. Attempting to revert message status to unresolved so it can be tried again.", handlingContext.Error.Exception);

                    using (var session = store.OpenAsyncSession())
                    {
                        var failedMessage = await session.LoadAsync<FailedMessage>(FailedMessage.MakeDocumentId(messageUniqueId))
                            .ConfigureAwait(false);
                        if (failedMessage != null)
                        {
                            failedMessage.Status = FailedMessageStatus.Unresolved;
                        }

                        var failedMessageRetry = await session.LoadAsync<FailedMessageRetry>(FailedMessageRetry.MakeDocumentId(messageUniqueId))
                            .ConfigureAwait(false);
                        if (failedMessageRetry != null)
                        {
                            session.Delete(failedMessageRetry);
                        }

                        await session.SaveChangesAsync()
                            .ConfigureAwait(false);
                    }

                    string reason;
                    try
                    {
                        reason = handlingContext.Error.Exception.GetBaseException().Message;
                    }
                    catch (Exception)
                    {
                        reason = "Failed to retrieve reason!";
                    }

                    await domainEvents.Raise(new MessagesSubmittedForRetryFailed
                    {
                        Reason = reason,
                        FailedMessageId = messageUniqueId,
                        Destination = destination
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // If something goes wrong here we just ignore, not the end of the world!
                    Log.Error("A failure occurred when trying to handle a retry failure.", ex);
                }
                finally
                {
                    executeOnFailure();
                }

                return ErrorHandleResult.Handled;
            }

            readonly Action executeOnFailure;
            IDocumentStore store;
            IDomainEvents domainEvents;
            static readonly ILog Log = LogManager.GetLogger(typeof(CaptureIfMessageSendingFails));
        }

    }
}