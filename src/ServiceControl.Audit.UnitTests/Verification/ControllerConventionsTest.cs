﻿namespace ServiceControl.Audit.UnitTests.Verification
{
    using System.Linq;
    using System.Web.Http.Controllers;
    using System.Web.Http.Dispatcher;
    using NUnit.Framework;
    using ServiceControl.Audit.Infrastructure.Settings;

    [TestFixture]
    public class ControllerConventionsTest
    {
        [Test]
        public void All_controllers_should_match_convention()
        {
            var allControllers = typeof(Settings).Assembly.GetTypes().Where(t => typeof(IHttpController).IsAssignableFrom(t)).ToArray();
            Assert.IsNotEmpty(allControllers);
            Assert.IsTrue(allControllers.All(c => c.Name.EndsWith(DefaultHttpControllerSelector.ControllerSuffix)));
        }
    }
}