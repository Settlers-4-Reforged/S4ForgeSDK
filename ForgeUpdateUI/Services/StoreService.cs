﻿using ForgeUpdater;
using ForgeUpdater.Manifests;

using ForgeUpdateUI.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForgeUpdateUI.Services {
    public class StoreService {
        public List<StoreInstallation<Manifest>> Stores { get; } = new List<StoreInstallation<Manifest>>();

        private LoggerService loggerService;

        public StoreService(LoggerService loggerService) {
            this.loggerService = loggerService;

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2) {
                Console.WriteLine("Usage: ForgeUpdateUI --store='<store_path>'");
                return;
            }

            List<string> stores = (from arg in args where arg.StartsWith("--store=") select arg.Substring(8).Trim('\'')).ToList();
            if (stores.Count == 0) {
                Console.WriteLine("Usage: ForgeUpdateUI --store='<store_path>'");
                return;
            }

            foreach (string storePath in stores) {
                loggerService.LogInfo($"Loading store from {storePath}");
                StoreInstallation<Manifest> installation = new StoreInstallation<Manifest>(storePath);
                Stores.Add(installation);
            }
        }

        public async Task ReadStoreState() {
            await Task.WhenAll(Stores.Select(store => store.ReadStoreState()));
        }



        public async IAsyncEnumerable<UpdateItem> UpdateAll() {
            foreach (var store in Stores) {
                var updates = store.ManifestsToUpdate.ToArray();

                if (updates.Length == 0) {
                    loggerService.LogInfo("No updates available for Store '{0}'", store.Store.Name);
                }

                Manifest? source = null;
                string patchString = "";
                await foreach (var update in store.UpdateAll()) {
                    if (source == null || source.Id != update.target.Id) {
                        source = updates.First(u => u.newer.Id == update.target.Id).source;
                        if (source == null) {
                            source = update.target;
                            patchString = $"->{update.target.Version}";
                        } else {
                            patchString = $"{source.Version}->{update.target.Version}";
                        }
                    }

                    yield return new UpdateItem() {
                        Name = update.target.Name,
                        Version = patchString,
                        Progress = update.Item2
                    };

                    await Task.Delay(1000);
                }
            }
        }
    }
}
