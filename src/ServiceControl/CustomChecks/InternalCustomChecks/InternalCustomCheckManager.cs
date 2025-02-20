﻿namespace ServiceControl.CustomChecks
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Contracts.CustomChecks;
    using Contracts.Operations;
    using Infrastructure.BackgroundTasks;
    using NServiceBus.CustomChecks;
    using NServiceBus.Logging;

    class InternalCustomCheckManager
    {
        public InternalCustomCheckManager(
            ICustomCheck check,
            EndpointDetails localEndpointDetails,
            IAsyncTimer scheduler,
            CustomCheckResultProcessor checkResultProcessor)
        {
            this.check = check;
            this.localEndpointDetails = localEndpointDetails;
            this.scheduler = scheduler;
            this.checkResultProcessor = checkResultProcessor;
        }

        public void Start()
        {
            timer = scheduler.Schedule(
                Run,
                TimeSpan.Zero,
                check.Interval ?? TimeSpan.MaxValue,
                e => { /* Should not happen */ }
            );
        }

        async Task<TimerJobExecutionResult> Run(CancellationToken cancellationToken)
        {
            CheckResult result;
            try
            {
                result = await check.PerformCheck()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var reason = $"`{check.GetType()}` implementation failed to run.";
                result = CheckResult.Failed(reason);
                Logger.Error(reason, ex);
            }

            var detail = new CustomCheckDetail
            {
                OriginatingEndpoint = localEndpointDetails,
                CustomCheckId = check.Id,
                Category = check.Category,
                HasFailed = result.HasFailed,
                FailureReason = result.FailureReason
            };

            await checkResultProcessor.ProcessResult(detail).ConfigureAwait(false);

            return check.Interval.HasValue
                ? TimerJobExecutionResult.ScheduleNextExecution
                : TimerJobExecutionResult.DoNotContinueExecuting;
        }

        public Task Stop() => timer?.Stop() ?? Task.CompletedTask;

        TimerJob timer;
        readonly ICustomCheck check;
        readonly EndpointDetails localEndpointDetails;
        readonly IAsyncTimer scheduler;
        readonly CustomCheckResultProcessor checkResultProcessor;

        static ILog Logger = LogManager.GetLogger<InternalCustomCheckManager>();
    }
}