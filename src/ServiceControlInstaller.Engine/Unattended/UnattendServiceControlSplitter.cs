﻿namespace ServiceControlInstaller.Engine.Unattended
{
    using System;
    using System.Threading.Tasks;
    using Accounts;
    using Configuration.ServiceControl;
    using Instances;
    using Validation;

    public class UnattendServiceControlSplitter
    {
        readonly ILogging log;
        UnattendServiceControlInstaller serviceControlInstaller;
        UnattendAuditInstaller auditInstaller;

        public UnattendServiceControlSplitter(ILogging loggingInstance, string deploymentCachePath)
        {
            log = loggingInstance;
            serviceControlInstaller = new UnattendServiceControlInstaller(loggingInstance, deploymentCachePath);
            auditInstaller = new UnattendAuditInstaller(loggingInstance, deploymentCachePath);
        }

        public async Task<Result> Split(ServiceControlInstance instance, Options options, Func<PathInfo, Task<bool>> pathToProceed)
        {
            var result = ValidateLicense();
            if (!result.Succeeded)
            {
                return result;
            }

            result = ValidateUpgradeVersion(instance);
            if (!result.Succeeded)
            {
                return result;
            }

            result = ValidateUpgradeAction(instance);
            if (!result.Succeeded)
            {
                return result;
            }

            log.Info("Gathering details for new audit instance...");
            var newAuditInstance = CreateSplitOutAuditInstance(instance);
            options.ApplyTo(newAuditInstance);

            result = ValidateServiceAccount(options, newAuditInstance);
            if (!result.Succeeded)
            {
                return result;
            }

            log.Info($"Upgrading existing instance {instance.Name}...");
            var serviceControlUpgradeOptions = new ServiceControlUpgradeOptions
            {
                UpgradeInfo = UpgradeControl.GetUpgradeInfoForTargetVersion(serviceControlInstaller.ZipInfo.Version, instance.Version),
                RemoteUrl = newAuditInstance.Url,
                DisableFullTextSearchOnBodies = options.DisableFullTextSearchOnBodies
            };

            if (!serviceControlInstaller.Upgrade(instance, serviceControlUpgradeOptions))
            {
                return Result.Failed($"Unable to upgrade existing instance {instance.Name}");
            }

            log.Info($"Creating new audit instance {newAuditInstance.Name}...");
            if (!await auditInstaller.Add(newAuditInstance, pathToProceed).ConfigureAwait(false))
            {
                return Result.Failed("Unable to create new audit instance");
            }

            return Result.Success;
        }

        Result ValidateUpgradeAction(ServiceControlInstance instance)
        {
            var recommendedUpgradeAction = instance.GetRequiredUpgradeAction(serviceControlInstaller.ZipInfo.Version);
            switch (recommendedUpgradeAction)
            {
                case RequiredUpgradeAction.Upgrade:
                    return Result.Failed("This instance cannot have an Audit instance split from it. Upgrade the instance instead", RequiredUpgradeAction.Upgrade);
                case RequiredUpgradeAction.ConvertToAudit:
                    return Result.Failed("This instance cannot have an Audit instance split from it as it has error ingestion disabled. Please contact support", RequiredUpgradeAction.ConvertToAudit);
                case RequiredUpgradeAction.SplitOutAudit:
                    return Result.Success;
                default:
                    return Result.Failed("This instance cannot have an Audit instance split from it. This instance has no recommended upgrade action");
            }
        }

        Result ValidateUpgradeVersion(ServiceControlInstance instance)
        {
            var upgradeInfo = UpgradeControl.GetUpgradeInfoForTargetVersion(serviceControlInstaller.ZipInfo.Version, instance.Version);
            if (instance.Version < upgradeInfo.CurrentMinimumVersion)
            {
                return Result.Failed($"An interim upgrade to version {upgradeInfo.RecommendedUpgradeVersion} is required before upgrading to version {serviceControlInstaller.ZipInfo.Version}. Download available at https://github.com/Particular/ServiceControl/releases/tag/{upgradeInfo.RecommendedUpgradeVersion}");
            }

            return Result.Success;
        }

        Result ValidateServiceAccount(Options options, ServiceControlAuditNewInstance auditDetails)
        {
            var account = UserAccount.ParseAccountName(auditDetails.ServiceAccount);
            if (!account.CheckPassword(auditDetails.ServiceAccountPwd))
            {
                return Result.Failed(
                    string.IsNullOrWhiteSpace(options.ServiceAccountPassword)
                        ? $"A password is required for the service account: {auditDetails.ServiceAccount}"
                        : $"Incorrect password for {auditDetails.ServiceAccount}");
            }

            return Result.Success;
        }

        Result ValidateLicense()
        {
            var licenseCheck = auditInstaller.CheckLicenseIsValid();
            if (!licenseCheck.Valid)
            {
                return Result.Failed(licenseCheck.Message);
            }

            return Result.Success;
        }

        ServiceControlAuditNewInstance CreateSplitOutAuditInstance(ServiceControlInstance source)
        {
            var instance = ServiceControlAuditNewInstance.CreateWithDefaultPersistence();

            instance.AuditQueue = source.AuditQueue;
            instance.AuditLogQueue = source.AuditLogQueue;
            instance.ForwardAuditMessages = source.ForwardAuditMessages;
            instance.AuditRetentionPeriod = source.AuditRetentionPeriod.Value;
            instance.TransportPackage = source.TransportPackage;
            instance.ConnectionString = source.ConnectionString;
            instance.HostName = source.HostName;
            instance.Name = $"{source.Name}.Audit";
            instance.ServiceAccount = source.Service.Account;
            // NOTE: The password should always be blank, as we don't read it back
            instance.ServiceAccountPwd = source.ServiceAccountPwd;
            instance.DisplayName = $"{source.Service.DisplayName}.Audit";
            instance.ServiceDescription = $"{source.Service.Description} (Audit)";
            instance.ServiceControlQueueAddress = source.Name;
            instance.EnableFullTextSearchOnBodies = source.EnableFullTextSearchOnBodies;

            return instance;
        }

        public class Options
        {
            public string InstallPath { get; set; }
            public string DBPath { get; set; }
            public string LogPath { get; set; }
            public int Port { get; set; }
            public int DatabaseMaintenancePort { get; set; }
            public bool DisableFullTextSearchOnBodies { get; set; }

            public string ServiceAccountPassword { get; set; }

            public void ApplyTo(ServiceControlAuditNewInstance instance)
            {
                instance.InstallPath = InstallPath;
                instance.DBPath = DBPath;
                instance.LogPath = LogPath;
                instance.Port = Port;
                instance.DatabaseMaintenancePort = DatabaseMaintenancePort;
                if (DisableFullTextSearchOnBodies)
                {
                    instance.EnableFullTextSearchOnBodies = false;
                }

                if (!string.IsNullOrWhiteSpace(ServiceAccountPassword))
                {
                    instance.ServiceAccountPwd = ServiceAccountPassword;
                }
            }
        }

        public class Result
        {
            public bool Succeeded { get; set; }
            public string FailureReason { get; set; }
            public RequiredUpgradeAction? RequiredUpgradeAction { get; set; }

            public static Result Success = new Result { Succeeded = true };

            public static Result Failed(string reason, RequiredUpgradeAction? requiredUpgradeAction = null)
                => new Result { FailureReason = reason, RequiredUpgradeAction = requiredUpgradeAction };
        }

    }
}