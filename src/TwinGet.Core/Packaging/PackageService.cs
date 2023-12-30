﻿// This file is licensed to you under MIT license.

using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Versioning;
using TwinGet.Core.Commands;
using TwinGet.TwincatInterface;
using TwinGet.TwincatInterface.Dto;
using TwinGet.TwincatInterface.ProjectFileUtils;
using TwinGet.TwincatInterface.Utils;
using static NuGet.Configuration.NuGetConstants;
using Task = System.Threading.Tasks.Task;

namespace TwinGet.Core.Packaging
{
    public class PackageService : IPackageService
    {
        public PackageService() { }

        public bool Pack(IPackCommand packCommand)
        {
            return PackAsync(packCommand).Result;
        }

        /// <summary>
        /// Package a PLC project into a NuGet package.
        /// </summary>
        /// <param name="packCommand"></param>
        /// <returns>True if successful, otherwise false.</returns>
        public async Task<bool> PackAsync(IPackCommand packCommand)
        {
            ArgumentException.ThrowIfNullOrEmpty(packCommand.Path, nameof(packCommand.Path));
            ArgumentException.ThrowIfNullOrEmpty(
                packCommand.OutputDirectory,
                nameof(packCommand.OutputDirectory)
            );

            if (TwincatUtils.IsPlcProjectFileExtension(packCommand.Path))
            {
                return await PackFromProjectFileAsync(packCommand);
            }
            else if (Utils.IsNuspecExtension(packCommand.Path))
            {
                return await PackFromNuspecFileAsync(packCommand);
            }

            return true;
        }

        private Task<bool> PackFromNuspecFileAsync(IPackCommand packCommand) =>
            throw new NotImplementedException();

        private static async Task<bool> PackFromProjectFileAsync(IPackCommand packCommand)
        {
            string? plcLibrary = SavePlcLibrary(packCommand);

            if (string.IsNullOrEmpty(plcLibrary))
            {
                packCommand.Logger?.LogError(
                    PackagingErrors.FailedToSavePlcLibrary,
                    packCommand.Path
                );
                return false;
            }

            bool result = BuildPackage(packCommand, plcLibrary);

            // Delete the library once we are done packing.
            if (!string.IsNullOrEmpty(plcLibrary))
            {
                File.Delete(plcLibrary);
            }

            return result;
        }

        private static bool BuildPackage(IPackCommand packCommand, string libraryPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(libraryPath, nameof(libraryPath));

            var plcProjectData = TwincatUtils.DeserializeXmlFileToProjectData<PlcProjectData>(
                packCommand.Path
            );

            ManifestMetadata metadata =
                new()
                {
                    Authors = new List<string>()
                    {
                        plcProjectData.PropertyGroup.Author ?? string.Empty
                    },
                    Version = new NuGetVersion(
                        plcProjectData.PropertyGroup.ProjectVersion ?? string.Empty
                    ),
                    Id = plcProjectData.PropertyGroup.Title,
                    Description = plcProjectData.PropertyGroup.Description,
                };

            var files = new List<ManifestFile>()
            {
                new() { Source = libraryPath, Target = "lib" }
            };

            if (!Directory.Exists(packCommand.OutputDirectory))
            {
                Directory.CreateDirectory(packCommand.OutputDirectory);
            }

            var packageBuilder = new PackageBuilder();
            packageBuilder.Populate(metadata);
            packageBuilder.PopulateFiles(string.Empty, files);

            string outputPath = Path.Combine(
                packCommand.OutputDirectory,
                $"{packageBuilder.Id}{PackageExtension}"
            );
            using FileStream stream = File.Open(outputPath, FileMode.OpenOrCreate);
            packageBuilder.Save(stream);

            packCommand.Logger?.LogInformation(PackagingStrings.PackSuccess, outputPath);

            return true;
        }

        private static string? SavePlcLibrary(IPackCommand packCommand)
        {
            string libraryPath = string.Empty;

            Task<string>? getSolutionTask = null;
            if (string.IsNullOrEmpty(packCommand.Solution))
            {
                getSolutionTask = GetParentSolutionFileAsync(packCommand.Path);
            }

            var thread = new Thread(async () =>
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                using AutomationInterface ai = new();
                string resolvedSolution = packCommand.Solution;

                // If no solution is provided, we try to find it.
                if (string.IsNullOrEmpty(resolvedSolution))
                {
                    await SafeExecuteAsync(
                        async () => resolvedSolution = await getSolutionTask,
                        packCommand.Logger
                    );

                    // If we cannot find the solution, quit early.
                    if (string.IsNullOrEmpty(resolvedSolution))
                    {
                        packCommand.Logger?.LogError(
                            PackagingErrors.FailedToResolveSolutionFile,
                            packCommand.Path
                        );
                        return;
                    }
                }

                packCommand.Logger?.LogInformation(
                    PackagingStrings.SavingPlcLibrary,
                    packCommand.Path
                );

                lock (libraryPath) // Just in case.
                {
                    SafeExecute(
                        () =>
                            libraryPath = ai.SavePlcProject(
                                packCommand.Path,
                                packCommand.OutputDirectory,
                                resolvedSolution
                            ),
                        packCommand.Logger
                    );
                }

#pragma warning restore CS8602 // Dereference of a possibly null reference.
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return libraryPath;
        }

        private static bool SafeExecute(Action action, ILogger? logger)
        {
            try
            {
                action.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHandler(logger, ex);
            }

            return false;
        }

        private static async Task<bool> SafeExecuteAsync(Func<Task> func, ILogger? logger)
        {
            try
            {
                await func();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHandler(logger, ex);
            }

            return false;
        }

        private static void ExceptionHandler(ILogger? logger, Exception ex)
        {
            logger?.LogError(ex.Message);
        }

        private static async Task<string> GetParentSolutionFileAsync(string plcProjectPath)
        {
            var helper = PlcProjectFileHelper.Create(plcProjectPath);
            return await helper.GetParentSolutionFileAsync();
        }
    }
}
