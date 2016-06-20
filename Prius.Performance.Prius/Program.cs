﻿using System;
using System.IO;
using Ioc.Modules;
using Microsoft.Practices.Unity;
using Prius.Contracts.Interfaces;
using Prius.Contracts.Interfaces.External;
using Prius.Contracts.Interfaces.Factory;
using Prius.Orm.Commands;
using Prius.Orm.Connections;
using Prius.Orm.Enumeration;
using Prius.Orm.Results;
using Prius.Performance.Prius.Integration;
using Prius.Performance.Prius.Model;
using Prius.Performance.Shared;
using Urchin.Client.Data;
using Urchin.Client.Interfaces;
using Urchin.Client.Sources;

namespace Prius.Performance.Prius
{
    class Program
    {
        static void Main(string[] args)
        {
            var container = ConfigureIoc();

            // Load the urchin config file. Prius uses Urchin for configuration
            var urchinConfigFile = container.Resolve<FileSource>();
            urchinConfigFile.Initialize(new FileInfo("urchin.json"), TimeSpan.FromSeconds(10));

            // Get our Prius implementation of the data access layer
            var dal = container.Resolve<IDataAccessLayer>();

            // Get the shared performance test runner
            var test = new PerformanceTest(dal);

            // Run all the performance tests and output results to the console
            using (var output = Console.OpenStandardOutput())
            {
                using (var writer = new StreamWriter(output))
                {
                    test.RunTests(writer);
                }
            }
            Console.ReadKey();
        }

        private static UnityContainer ConfigureIoc()
        {
            var container = new UnityContainer();
            container.RegisterInstance(container);

            var packageLocator = new PackageLocator().ProbeBinFolderAssemblies();
            Ioc.Modules.Unity.Registrar.Register(packageLocator, container);

            return container;
        }
    }
}
