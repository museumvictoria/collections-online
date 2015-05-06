﻿using System;
using AutoMapper;
using CollectionsOnline.Core.Config;
using CollectionsOnline.Core.Infrastructure;
using CollectionsOnline.Core.Models;
using CollectionsOnline.Import.Imports;
using CollectionsOnline.Import.Infrastructure;
using Ninject;
using Ninject.Extensions.Conventions;
using NLog;
using Raven.Client;

namespace CollectionsOnline.Import
{
    class Program
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static IKernel _kernel;

        public static volatile bool ImportCanceled = false;

        static void Main(string[] args)
        {
            // Set up Ctrl+C handling 
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                _log.Warn("Canceling all imports");

                eventArgs.Cancel = true;
                ImportCanceled = true;
            };

            // Log any exceptions that are not handled
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => _log.Error(eventArgs.ExceptionObject);                                    

            _kernel = CreateKernel();

            _kernel.Get<ImportRunner>().Run();
        }

        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();

            RegisterServices(kernel);

            return kernel;
        }

        private static void RegisterServices(IKernel kernel)
        {
            // Raven Db Bindings
            kernel.Bind<IDocumentStore>().ToProvider<NinjectRavenDocumentStoreProvider>().InSingletonScope();

            // Bind Imports
            kernel.Bind<IImport>().To<ImuImport<Article>>();
            kernel.Bind<IImport>().To<ImuImport<Species>>();
            kernel.Bind<IImport>().To<ImuImport<Item>>();
            kernel.Bind<IImport>().To<ImuImport<Specimen>>();
            kernel.Bind<IImport>().To<ImuImport<CollectionOverview>>();

            // Bind the rest
            kernel.Bind(x => x
                .FromAssemblyContaining(typeof(Program), typeof(Constants))
                .SelectAllClasses()
                .BindAllInterfaces());

            // Automapper configuration for update methods on EmuAggregateRootFactories
            Mapper.Initialize(cfg =>
            {
                cfg.CreateMap<Article, Article>()
                    .ForMember(x => x.Id, options => options.Ignore());
                cfg.CreateMap<Item, Item>()
                    .ForMember(x => x.Id, options => options.Ignore())
                    .ForMember(x => x.Comments, options => options.Ignore());
                cfg.CreateMap<Species, Species>()
                    .ForMember(x => x.Id, options => options.Ignore());
                cfg.CreateMap<Specimen, Specimen>()
                    .ForMember(x => x.Id, options => options.Ignore());
                cfg.CreateMap<CollectionOverview, CollectionOverview>()
                    .ForMember(x => x.Id, options => options.Ignore());
            });
        }
    }
}