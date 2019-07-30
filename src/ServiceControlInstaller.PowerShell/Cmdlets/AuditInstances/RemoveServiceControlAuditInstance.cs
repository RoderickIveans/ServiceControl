// ReSharper disable UnassignedField.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace ServiceControlInstaller.PowerShell
{
    using System.IO;
    using System.Management.Automation;
    using Engine.Instances;
    using Engine.Unattended;

    [Cmdlet(VerbsCommon.Remove, "ServiceControlAuditInstance")]
    public class RemoveServiceControlAuditInstance : PSCmdlet
    {
        [ValidateNotNullOrEmpty]
        [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0, HelpMessage = "Specify the ServiceControl Audit instance name to remove")]
        public string[] Name { get; set; }

        [Parameter(HelpMessage = "Remove the RavenDB database")]
        public SwitchParameter RemoveDB { get; set; }

        [Parameter(HelpMessage = "Remove the Logs directory")]
        public SwitchParameter RemoveLogs { get; set; }

        protected override void BeginProcessing()
        {
            Account.TestIfAdmin();
        }

        protected override void ProcessRecord()
        {
            var logger = new PSLogger(Host);
            var zipfolder = Path.GetDirectoryName(MyInvocation.MyCommand.Module.Path);
            var installer = new UnattendAuditInstaller(logger, zipfolder);

            foreach (var name in Name)
            {
                var instance = InstanceFinder.FindServiceControlInstance(name);
                if (instance == null)
                {
                    WriteWarning($"No action taken. An instance called {name} was not found");
                    break;
                }

                WriteObject(installer.Delete(instance.Name, RemoveDB.ToBool(), RemoveLogs.ToBool()));
            }
        }
    }
}