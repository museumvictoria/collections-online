﻿using System;
using System.Collections.Generic;
using System.Linq;
using CollectionsOnline.Core.Config;

namespace CollectionsOnline.Core.Models
{
    public class Application : AggregateRoot
    {
        public bool ImportsRunning { get; set; }

        public IList<ImportStatus> ImportStatuses { get; set; }

        public Application()
        {
            Id = Constants.ApplicationId;

            InitializeCollections();
        }

        public void RunAllImports()
        {
            ImportsRunning = true;
        }

        public void FinishedAllImports()
        {
            ImportsRunning = false;
        }

        public void FinishedAllImportsSuccessfully()
        {
            ImportsRunning = false;

            foreach (var importStatus in ImportStatuses)
            {
                importStatus.IsFinished = false;
                importStatus.CurrentImportCacheOffset = 0;
            }
        }

        public void ImportFinished(string importType, DateTime importCacheDateCreated)
        {
            var importStatus = ImportStatuses.First(x => x.ImportType == importType);

            importStatus.IsFinished = true;
            importStatus.PreviousDateRun = importCacheDateCreated;
        }

        public ImportStatus GetImportStatus(string importType)
        {
            var importStatus = ImportStatuses.FirstOrDefault(x => x.ImportType == importType);

            if (importStatus == null)
            {
                importStatus = new ImportStatus
                {
                    ImportType = importType
                };

                ImportStatuses.Add(importStatus);
            }
            
            return importStatus;
        }

        private void InitializeCollections()
        {
            ImportStatuses = new List<ImportStatus>();
        }
    }
}