using Humanizer;
using Intent.NuGetReferenceUpdater.NuGetConfig;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Intent.IArchitect.Agent.Persistence.Model;
using Intent.IArchitect.Common.Publishing;
using Serilog;
using static System.Net.Mime.MediaTypeNames;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using NuGet.Frameworks;
using System.Xml.Linq;
using Intent.IArchitect.Agent.Persistence.Model.Common;
using static Intent.Modules.ModuleBuilder.CSharp.Tasks.NuGetApi;
using Intent.Modules.ModuleBuilder.CSharp.Tasks;

namespace Intent.NuGetReferenceUpdater
{
    internal class FileUpdater
    {
        private readonly string _islnfileName;
        private readonly ParseResult _parse;
        private Dictionary<string, List<NugetVersionInfo>> _nuGetCache;

        private const string PackageVersionSettingsStereoTypeId = "7af88c37-ce54-49fc-b577-bde869c23462";
        private const string PackageElementTypeId = "f747cc37-29ee-488a-8dbe-755e856a842d";
        private const string PackageVersionElementTypeId = "231f8cf8-517b-4801-9682-991d22f4e662";
        private const string PackageSettingsStereoTypeId = "265221a5-779c-46c9-a367-8b07b435803b";
        private const string NuGetDependecnyElementTypeId = "3097322a-a058-4058-beed-4fcd6272f61d";       

        
        public FileUpdater(
            ParseResult parse,
            string userName,
            string password,
            string islnfileName
)
        {
            _parse = parse; 
            _islnfileName = islnfileName;
            _nuGetCache = new Dictionary<string, List<NugetVersionInfo>>();
        }
        public async Task UpdateFilesAsync(string resumeid = null, CancellationToken cancellationToken = default)
        {
            DomainPublisher.Set(new DummyDomainPublisher());

            var loggerConfiguration = new LoggerConfiguration();
            loggerConfiguration = loggerConfiguration
                .WriteTo.Console()
                .Filter.ByExcluding(@event =>
                    @event.MessageTemplate.Text == "'\\' detected in path, please rather use platform agnostic '/': {path}");

            loggerConfiguration = loggerConfiguration.MinimumLevel.Debug();
            Log.Logger = loggerConfiguration.CreateLogger();

            var islnName = _islnfileName;

            if (islnName is null)
            {
                throw new FileNotFoundException(
                    $"Could not 'isln' : ({islnName})");

            }

            await UpdateApplicationNuGetPackages(islnName, resumeid, cancellationToken);
        }

        private async Task UpdateApplicationNuGetPackages(string? islnName, string? resumeId, CancellationToken cancellationToken = default)
        {

            var solution = SolutionPersistable.Load(islnName);
            if (solution == null) throw new Exception("Loaded isln file is null.");

            foreach (var application in solution.GetApplications())
            {
                if (resumeId is not null)
                {
                    if (application.Id != resumeId)
                    {
                        continue;
                    }
                    else
                    {
                        resumeId = null;
                    }

                }
                Console.WriteLine($"Checking application : {application.Name}");

                var designer = application.GetDesigners().SingleOrDefault(x => x.Name == "Module Builder");
                if (designer == null) continue;

                var designerPackages = designer
                    .GetPackages(includeExternal: false, packageFileOnly: true)
                    .ToArray();
                if (designerPackages.Length != 1)
                {
                    throw new Exception($"Multiple packages found, must be specified when more than one package exists in the designer");
                }

                var package = designerPackages[0];
                var imodspec = Directory.GetFiles(application.GetOutputLocation(), $"{package.Name}.imodspec", SearchOption.AllDirectories).FirstOrDefault();

                if (imodspec == null)
                {
                    throw new Exception($"Can't find {package.Name}.imodspec");
                }

                if (package == null) throw new Exception("Package is null.");

                if (!package.IsFullyLoaded)
                {
                    package.Load();
                }
                //NuGet Packages
                var packages = package.Classes.Where(c => c.SpecializationTypeId == PackageElementTypeId);
                if (!packages.Any())
                {
                    continue;
                }

                Console.WriteLine("Checking there is no pending changes");
                await RunSFCLI("ensure-no-outstanding-changes", application.Id, cancellationToken);

                bool changes = false;
                foreach (var child in packages)
                {
                    if (child == null) continue; 

                    //Ignore Locked Packages
                    if (child.Stereotypes.FirstOrDefault(s => s.DefinitionId == PackageSettingsStereoTypeId)?.Properties.FirstOrDefault(p => p.Name == "Locked")?.Value == "true")
                    {
                        continue;
                    }
                    var packageName = child.Name;
                    if (!_nuGetCache.TryGetValue(packageName, out var nugetDetails))
                    {
                        //Cache NuGet results in case different modules have the same packages no need to request same data given api limiting
                        nugetDetails = await NuGetApi.GetLatestVersionsForFrameworksAsync(packageName);
                        _nuGetCache[packageName] = nugetDetails;
                    }
                    foreach (var x in nugetDetails)
                    {
                        var updateDetails = GetVersionDetails(child.ChildElements, x);

                        //Version already exists and (is locked or is the current lastest version)
                        if (updateDetails != null && (updateDetails.Locked || updateDetails.Element.Name == updateDetails.PackageVersion))
                        {
                            continue;
                        }
                        Console.WriteLine($"Updating NuGet Package : {packageName}({application.Name})");
                        changes = true;

                        if (updateDetails == null)
                        {
                            var newVersion = CreatePackageVersion(child, x);
                            child.AddElement(newVersion);
                            UpdatePackageDependencies(newVersion, x);
                        }
                        else
                        {
                            updateDetails.Element.Name = updateDetails.PackageVersion;
                            UpdatePackageDependencies(updateDetails.Element, x);
                        }
                    }
                }
                if (changes)
                {
                    package.Save();
                    Console.WriteLine("Applying pending changes");
                    await RunSFCLI("apply-pending-changes", application.Id, cancellationToken);

                    Console.WriteLine($"Updating versions info : {imodspec}");
                    var releaseVersion = await UpdateIModSpecAsync(imodspec, null, default);
                    if (releaseVersion != null)
                    {
                        await UpdateReleaseNotesAsync(Path.GetDirectoryName(imodspec), releaseVersion, null, "- Improvement: Updated NuGet package versions.", default);
                    }

                }
            }
        }

        private void UpdatePackageDependencies(ElementPersistable element, NugetVersionInfo x)
        {
            element.ChildElements.Clear();
            foreach (var dependency in x.Dependencies)
            {
                var dependencyElement = new ElementPersistable()
                {
                    Id = Guid.NewGuid().ToString().ToLower(),
                    SpecializationType = "NuGet Dependency",
                    SpecializationTypeId = NuGetDependecnyElementTypeId,
                    Name = dependency.PackageName,
                    ParentFolderId = element.Id,
                    Value = dependency.Version.MinVersion?.ToString()
                };
                element.ChildElements.Add(dependencyElement);
            }
        }

        private static ElementPersistable CreatePackageVersion(ElementPersistable child, NugetVersionInfo x)
        {
            var newVersion = new ElementPersistable()
            {
                Id = Guid.NewGuid().ToString().ToLower(),
                SpecializationType = "Package Version",
                SpecializationTypeId = PackageVersionElementTypeId,
                Name = x.PackageVersion.ToString(),
                ParentFolderId = child.Id,
            };
            var packageVersionSettingsStereotype = new StereotypePersistable()
            {
                DefinitionId = PackageVersionSettingsStereoTypeId,
                Name = "Package Version Settings",
                DefinitionPackageId = "f2bfb0f7-d304-466f-b923-021d4016b48d",
                DefinitionPackageName = "Intent.ModuleBuilder.CSharp",
                Properties = new List<StereotypePropertyPersistable>()
                    {
                        new StereotypePropertyPersistable()
                        {
                            Id = "b01cea92-0ca1-4dbe-acab-d0f52b39e003",
                            Name = "Minimum Target Framework",
                            Value = x.FrameworkVersion.DotNetFrameworkName,
                            IsActive = true,
                        },
                        new StereotypePropertyPersistable()
                        {
                            Id = "d00692b1-6d17-4f1e-9386-a1d0d3ab7b57",
                            Name = "Locked",
                            Value = "false",
                            IsActive = true,
                        }
                    }
            };

            newVersion.Stereotypes.Add(packageVersionSettingsStereotype);
            return newVersion;
        }

        private VersionDetails? GetVersionDetails(IEnumerable<ElementPersistable> children, NugetVersionInfo nugetInfo)
        {
            var versionElement = children.FirstOrDefault(x => x.SpecializationTypeId == PackageVersionElementTypeId && 
                x.Stereotypes.FirstOrDefault(s => s.DefinitionId == PackageVersionSettingsStereoTypeId)?.Properties.FirstOrDefault(p => p.Name == "Minimum Target Framework")?.Value == nugetInfo.FrameworkVersion.DotNetFrameworkName);
            if (versionElement is null)
                return null;
            bool locked = versionElement.Stereotypes.FirstOrDefault(s => s.DefinitionId == PackageVersionSettingsStereoTypeId)?.Properties.FirstOrDefault(p => p.Name == "Locked")?.Value == "true";
            return new VersionDetails(nugetInfo.PackageVersion.ToString(), locked, versionElement);
        }

        private record VersionDetails(string PackageVersion, bool Locked, ElementPersistable Element );


        private async Task RunSFCLI(string command, string applicationId, CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "intent-cli",
                Arguments = new ProcessArgumentBuilder(_parse)
                    .WithArgument(command)
                    .WithArgument(Symbols.Arguments.UsernameArgument)
                    .WithArgument(Symbols.Arguments.PasswordArgument)
                    .WithArgument(Symbols.Arguments.IslnPathArgument)
                    .WithOption(Symbols.Options.ApplicationIdOption, applicationId)
                    .WithOption(Symbols.Options.AccessToken)
                    .Build(),
                RedirectStandardOutput = true,
            };

            var process = Process.Start(startInfo)!;
            Console.WriteLine($"{startInfo.FileName} {startInfo.Arguments}");
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0)
            {
                return;
            }          
            else
            {
                throw new Exception($"Error running DF CLI {process.ExitCode} (Probably outstanding changes)");
            }
        }

        private async Task UpdateReleaseNotesAsync(string directory, string releaseVersion, NuGetDependencyConfig changedjsonFile, string releaseNote = "- Improvement: Updated NuGet packages to latest stables.", CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(directory, "release-notes.md");
            if (files.Length != 1)
            {
                Console.WriteLine($"Can't find `release-notes.md` in : {directory}");
                return;
            }
            string releaseNotesFileName = files[0];
            var lines = await File.ReadAllLinesAsync(releaseNotesFileName, cancellationToken);

            StringBuilder sb = new StringBuilder();
            bool versionExists = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!versionExists && line.Trim().StartsWith("### Version "))
                {
                    string version = line.Trim().Substring("### Version ".Length);
                    if (version == releaseVersion)
                    {
                        sb.AppendLine(line);
                        sb.AppendLine();
                        if (lines.Length > i + 1 && string.IsNullOrEmpty(lines[i + 1]))
                        {
                            i++;
                        }
                        if (lines.Length <= i + 1)
                        {
                            sb.AppendLine($"{releaseNote}");
                        }
                        else if (lines[i + 1] != releaseNote)
                        {
                            sb.AppendLine($"{releaseNote}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"### Version {releaseVersion}");
                        sb.AppendLine();
                        sb.AppendLine($"{releaseNote}");
                        sb.AppendLine();
                        sb.AppendLine(line);
                    }
                    versionExists = true;
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            Console.WriteLine($"Updating release notes");
            await File.WriteAllTextAsync(releaseNotesFileName, sb.ToString(), cancellationToken);
        }

        private async Task<string?> UpdateIModSpecAsync(string imodspecFile, NuGetDependencyConfig changedjsonFile, CancellationToken cancellationToken = default)
        {
            string content = await File.ReadAllTextAsync(imodspecFile, cancellationToken);

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(content);

            // Find the <version> element
            XmlNode versionNode = doc.SelectSingleNode("/package/version");

            // Check if the <version> element is found
            if (versionNode == null)
            {
                throw new Exception($"Cant find version in imodspec : {imodspecFile}");
            }

            var version = NuGetVersion.Parse(versionNode.InnerText);
            NuGetVersion newVersion;
            if (version.IsPrerelease)
            {
                string[] prereleaseParts = version.Release.Split('.');
                if (prereleaseParts.Length > 1 && int.TryParse(prereleaseParts[1], out int prereleaseNumber))
                {
                    // Increment the numeric part of the pre-release label
                    prereleaseParts[1] = (prereleaseNumber + 1).ToString();
                    string newPrerelease = string.Join('.', prereleaseParts);

                    // Create a new version with the incremented pre-release label
                    newVersion = new NuGetVersion(version.Version, newPrerelease);
                }
                else
                {
                    newVersion = new NuGetVersion(version.Major, version.Minor, version.Patch + 1, "pre.0");
                }
            }
            else
            {
                newVersion = new NuGetVersion(version.Major, version.Minor, version.Patch + 1, "pre.0");
            }

            versionNode.InnerText = newVersion.ToString();

            Console.WriteLine($"Updating .imodpsec : {newVersion.ToString()}");

            // Output the modified XML content with original formatting preserved
            using (var fileWriter = File.OpenWrite(imodspecFile))
            using (XmlWriter xmlWriter = XmlWriter.Create(fileWriter, new XmlWriterSettings { Indent = true, NewLineOnAttributes = false }))
            {
                doc.Save(xmlWriter);
            }
            var releaseVersion = new NuGetVersion(newVersion.Major, newVersion.Minor, newVersion.Patch);
            return releaseVersion.ToString();
        }


        private class DummyDomainPublisher : IDomainEventDispatcher
        {
            public Task<TResponse> Request<TResponse>(IDomainRequest<TResponse> request) => throw new NotSupportedException();
            public Task Publish(IDomainEvent @event) => Task.CompletedTask;
        }
    }
}
