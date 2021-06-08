﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using MassEffectModManagerCore.modmanager.windows;
using Newtonsoft.Json;
using Serilog;

namespace MassEffectModManagerCore.modmanager.objects.mod.merge.v1
{
    public class MergeFile1
    {
        [JsonProperty(@"filename")]
        public string FileName { get; set; }

        [JsonProperty("changes")]
        public List<MergeFileChange1> MergeChanges { get; set; }

        [JsonProperty("fileexistenceoptional")]
        public bool FileExistenceOptional { get; set; }
        [JsonProperty("applytoalllocalizations")] public bool ApplyToAllLocalizations { get; set; }

        [JsonIgnore] public MergeMod1 Parent;

        [JsonIgnore]
        public MergeMod1 OwningMM => Parent;

        public void SetupParent(MergeMod1 mm)
        {
            Parent = mm;
            foreach (var mc in MergeChanges)
            {
                mc.SetupParent(this);
            }
        }

        public void ApplyChanges(CaseInsensitiveDictionary<string> loadedFiles, Mod associatedMod, ref int numMergesCompleted, int numTotalMerges, Action<int, int, string, string> mergeProgressDelegate = null)
        {
            List<string> targetFiles = new List<string>();
            if (ApplyToAllLocalizations)
            {
                var targetnameBase = Path.GetFileNameWithoutExtension(FileName);
                var targetExtension = Path.GetExtension(FileName);
#if DEBUG
                var localizations = StarterKitGeneratorWindow.GetLanguagesForGame(associatedMod?.Game ?? MEGame.LE2);
#else
                var localizations = StarterKitGeneratorWindow.GetLanguagesForGame(associatedMod.Game);
#endif

                // Ensure end name is not present on base
                foreach (var l in localizations)
                {
                    if (targetnameBase.EndsWith($@"_{l}", StringComparison.InvariantCultureIgnoreCase))
                        targetnameBase = targetnameBase.Substring(0, targetnameBase.Length - 4);
                }

                foreach (var l in localizations)
                {
                    var targetname = $@"{targetnameBase}_{l}{targetExtension}";
                    if (loadedFiles.TryGetValue(targetname, out var fullpath))
                    {
                        targetFiles.Add(fullpath);
                    }
                    else
                    {
                        Log.Warning($@"File not found in game: {targetname}, skipping...");
                        numMergesCompleted++;
                        mergeProgressDelegate?.Invoke(numMergesCompleted, numMergesCompleted, null, null);
                    }
                }
            }
            else
            {
                if (loadedFiles.TryGetValue(FileName, out var fullpath))
                {
                    targetFiles.Add(fullpath);
                }
                else
                {
                    Log.Warning($@"File not found in game: {FileName}, skipping...");
                    numMergesCompleted++;
                    mergeProgressDelegate?.Invoke(numMergesCompleted, numMergesCompleted, null, null);
                }
            }


            MergeAssetCache1 mac = new MergeAssetCache1();
            foreach (var f in targetFiles)
            {
#if DEBUG
                Stopwatch sw = Stopwatch.StartNew();
#endif
                // Open as memorystream as we need to hash this file for tracking
                using MemoryStream ms = new MemoryStream(File.ReadAllBytes(f));
                
                var existingMD5 = Utilities.CalculateMD5(ms);
                var package = MEPackageHandler.OpenMEPackageFromStream(ms, f);
#if DEBUG
                Debug.WriteLine($@"Opening package {f} took {sw.ElapsedMilliseconds} ms");
#endif
                foreach (var pc in MergeChanges)
                {
                    pc.ApplyChanges(package, mac, associatedMod);
                }

                var track = package.IsModified;
                if (package.IsModified)
                {
                    Log.Information($@"Saving package {package.FilePath}");
#if DEBUG
                    sw.Restart();
#endif
                    package.Save(savePath: f, compress: true);
#if DEBUG
                    Debug.WriteLine($@"Saving package {f} took {sw.ElapsedMilliseconds} ms");
#endif
                }
                else
                {
                    Log.Information($@"Package {package.FilePath} was not modified. This change is likely already installed, not saving package");
                }

                numMergesCompleted++;
                mergeProgressDelegate?.Invoke(numMergesCompleted, numTotalMerges, track ? existingMD5 : null, track ? f : null);
            }
        }

        public int GetMergeCount() => ApplyToAllLocalizations ? StarterKitGeneratorWindow.GetLanguagesForGame(OwningMM.Game).Length : 1;


        /// <summary>
        /// Validates this MergeFile. Throws an exception if the validation fails.
        /// </summary>
        public void Validate()
        {
            if (FileName == null) throw new Exception("'filename' cannot be null for a merge file!");
            var safeFiles = EntryImporter.FilesSafeToImportFrom(OwningMM.Game);
            if (!safeFiles.Any(x => FileName.StartsWith(Path.GetFileNameWithoutExtension(x), StringComparison.InvariantCultureIgnoreCase)))
            {
                // Does this catch DLC startups?
                throw new Exception($"Cannot merge into non-startup file: {FileName}");
            }
        }
    }
}