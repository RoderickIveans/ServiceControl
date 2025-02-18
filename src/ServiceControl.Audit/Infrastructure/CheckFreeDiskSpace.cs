﻿namespace ServiceControl.Audit.Infrastructure
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using NServiceBus.CustomChecks;
    using NServiceBus.Logging;
    using ServiceControl.Audit.Infrastructure.Settings;

    class CheckFreeDiskSpace : CustomCheck
    {
        public CheckFreeDiskSpace(Settings.Settings settings) : base("ServiceControl.Audit database", "Storage space", TimeSpan.FromMinutes(5))
        {
            dataPath = SettingsReader<string>.Read("DbPath", null);
            percentageThreshold = settings.DataSpaceRemainingThreshold / 100m;
            Logger.Debug($"Check ServiceControl data drive space remaining custom check starting. Threshold {percentageThreshold:P0}");
        }

        public override Task<CheckResult> PerformCheck()
        {
            if (string.IsNullOrEmpty(dataPath))
            {
                return CheckResult.Pass;
            }

            var dataPathRoot = Path.GetPathRoot(dataPath);

            if (dataPathRoot == null)
            {
                throw new Exception($"Unable to find the root of the data path {dataPath}");
            }

            var dataDriveInfo = new DriveInfo(dataPathRoot);
            var availableFreeSpace = (decimal)dataDriveInfo.AvailableFreeSpace;
            var totalSpace = (decimal)dataDriveInfo.TotalSize;

            var percentRemaining = (decimal)dataDriveInfo.AvailableFreeSpace / dataDriveInfo.TotalSize;

            if (Logger.IsDebugEnabled)
            {
                Logger.Debug($"Free space: {availableFreeSpace} | Total: {totalSpace} | Percent remaining {percentRemaining:P0}");
            }

            if (percentRemaining > percentageThreshold)
            {
                return CheckResult.Pass;
            }

            var message = $"{percentRemaining:P0} disk space remaining on data drive '{dataDriveInfo.VolumeLabel} ({dataDriveInfo.RootDirectory})' on '{Environment.MachineName}'.";

            Logger.Warn(message);
            return CheckResult.Failed(message);
        }

        readonly string dataPath;
        decimal percentageThreshold;
        static readonly ILog Logger = LogManager.GetLogger(typeof(CheckFreeDiskSpace));
    }
}