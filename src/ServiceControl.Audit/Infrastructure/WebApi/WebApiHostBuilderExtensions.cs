﻿namespace ServiceControl.Audit.Infrastructure.WebApi
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Web.Http.Controllers;
    using Auditing.MessagesView;
    using Autofac;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using OWIN;

    static class WebApiHostBuilderExtensions
    {
        public static IHostBuilder UseWebApi(this IHostBuilder hostBuilder, string rootUrl, bool startOwinHost)
        {
            hostBuilder.ConfigureContainer<ContainerBuilder>(RegisterInternalWebApiControllers);
            hostBuilder.ConfigureContainer<ContainerBuilder>(cb => cb.RegisterModule<ApisModule>());

            if (startOwinHost)
            {
                hostBuilder.ConfigureServices((ctx, serviceCollection) =>
                {
                    serviceCollection.AddHostedService(sp =>
                    {
                        var startup = new Startup(sp.GetRequiredService<ILifetimeScope>());
                        return new WebApiHostedService(rootUrl, startup);
                    });
                });
            }

            return hostBuilder;
        }

        static void RegisterInternalWebApiControllers(ContainerBuilder containerBuilder)
        {
            var controllerTypes = Assembly.GetExecutingAssembly().DefinedTypes
                .Where(t => typeof(IHttpController).IsAssignableFrom(t) && t.Name.EndsWith("Controller", StringComparison.Ordinal));

            foreach (var controllerType in controllerTypes)
            {
                containerBuilder.RegisterType(controllerType);
            }
        }
    }
}