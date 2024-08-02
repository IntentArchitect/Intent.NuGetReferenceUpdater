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
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static Intent.NuGetReferenceUpdater.NuGetApi;

namespace Intent.NuGetReferenceUpdater
{
    internal class FileUpdater
    {
        private const string Filename = "NugetPackages.json";
        private readonly JsonSerializerOptions _serialziationOptions;
        private readonly bool _forceUpdates;

        public FileUpdater(bool forceUpdates)
        {
            _forceUpdates = forceUpdates;
            _serialziationOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task UpdateFilesAsync(string directory, CancellationToken cancellationToken = default)
        {

            var consolidation = await ConsolidateNugetRequestsAcrossFiles(directory);

            await UpdateNuGetPackages(consolidation.Packages);

            var changedFiles = consolidation.JsonFiles.Where(f => _forceUpdates || ( !_forceUpdates && f.Changed));

            await UpdateModuleFiles(changedFiles, cancellationToken);

        }

        private async Task<Consolidation> ConsolidateNugetRequestsAcrossFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new Exception($"Directory does not exist : {directory}");

            }
            Dictionary<string, ConsolidatedNugetUpdate> consolidatedNugetUpdates = new();
            List<NuGetDependencyConfig> jsonFiles = new();

            //Consolidate Nuget Requests
            var fileNames = Directory.GetFiles(directory, Filename, SearchOption.AllDirectories);
            foreach (string file in fileNames)
            {
                Console.WriteLine($"Gathering NuGet Dependencies : {file}");
                NuGetDependencyConfig config;
                var content = await File.ReadAllTextAsync(file);
                try
                {
                    config = JsonSerializer.Deserialize<NuGetDependencyConfig>(content, _serialziationOptions);
                    config.Initialize(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to deserialize : {file}{Environment.NewLine}{ex.GetBaseException().Message}");
                    continue;
                }

                jsonFiles.Add(config);
                foreach (var package in config.Packages)
                {
                    if (!consolidatedNugetUpdates.TryGetValue(package.Name, out var update))
                    {
                        update = new ConsolidatedNugetUpdate(package.Name, []);
                        consolidatedNugetUpdates.Add(package.Name, update);
                    }
                    update.Modules.Add(package);
                }
            }
            return new Consolidation(consolidatedNugetUpdates.Values.ToList(), jsonFiles);
        }
        private record Consolidation(List<ConsolidatedNugetUpdate> Packages, List<NuGetDependencyConfig> JsonFiles);
        private record ConsolidatedNugetUpdate(string PackageName, List<Package> Modules);

        private static async Task UpdateNuGetPackages(IList<ConsolidatedNugetUpdate> consolidatedNugetUpdates)
        {
            foreach (var update in consolidatedNugetUpdates)
            {
                var versionInfo = await NuGetApi.GetLatestVersionsForFrameworksAsync(update.PackageName);
                foreach (var package in update.Modules)
                {
                    if (package.Locked == true)
                    {
                        continue;
                    }
                    if (UpdatePackageInfo(package, versionInfo))
                    {
                        package.Changed();
                    }
                }
            }
        }

        private async Task UpdateModuleFiles(IEnumerable<NuGetDependencyConfig> changedFiles, CancellationToken cancellationToken)
        {
            foreach (var changedjsonFile in changedFiles)
            {
                var directory = Path.GetDirectoryName(changedjsonFile.Filename)!;
                await OverwriteNugetPackagesCSFileAsync(directory, changedjsonFile, cancellationToken);
                var releaseVersion = await UpdateIModSpecAsync(directory, changedjsonFile, cancellationToken);
                if (releaseVersion != null)
                {
                    await UpdateReleaseNotesAsync(directory, releaseVersion, changedjsonFile, cancellationToken);
                }
                await PersistNugetPackageJsonFileAsync(changedjsonFile, cancellationToken);
            }
        }

        private async Task OverwriteNugetPackagesCSFileAsync(string directoy, NuGetDependencyConfig changedjsonFile, CancellationToken cancellationToken)
        {
            string @namespace;
            var files = Directory.GetFiles(directoy, "*.csproj");
            if (files.Length > 0) 
            {
                @namespace = Path.GetFileNameWithoutExtension(files[0]);
            }
            else
            {
                string directoryPath = Path.GetDirectoryName(directoy);
                @namespace = new DirectoryInfo(directoryPath).Name;
            }
            StringBuilder content = new();
            content.AppendLine($@"using Intent.Engine;
using Intent.Modules.Common.VisualStudio;

namespace {@namespace}
{{
    public static class NugetPackages
    {{");
            foreach (var package in  changedjsonFile.Packages)
            {
                content.AppendLine($@"
        public static NugetPackageInfo {ToCSharpIdentifier(package.Name)}(IOutputTarget outputTarget) => new(
            name: ""{package.Name}"",
            version: outputTarget.GetMaxNetAppVersion() switch
            {{");
                AddVerions(content, package);
                content.AppendLine($"            }});");
            }
            content.AppendLine(@"    }
}");

            await File.WriteAllTextAsync(Path.Combine( directoy, "NugetPackages.cs"), content.ToString(), cancellationToken);
        }

        public static string ToCSharpIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            // https://docs.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names
            // - Identifiers must start with a letter, or _.
            // - Identifiers may contain Unicode letter characters, decimal digit characters,
            //   Unicode connecting characters, Unicode combining characters, or Unicode formatting
            //   characters. For more information on Unicode categories, see the Unicode Category
            //   Database. You can declare identifiers that match C# keywords by using the @ prefix
            //   on the identifier. The @ is not part of the identifier name. For example, @if
            //   declares an identifier named if. These verbatim identifiers are primarily for
            //   interoperability with identifiers declared in other languages.

            identifier = identifier
                .Replace("#", "Sharp")
                .Replace("&", "And");

            var asCharArray = identifier.ToCharArray();
            for (var i = 0; i < asCharArray.Length; i++)
            {
                // Underscore character is allowed
                if (asCharArray[i] == '_')
                {
                    continue;
                }

                switch (char.GetUnicodeCategory(asCharArray[i]))
                {
                    case UnicodeCategory.DecimalDigitNumber:
                    case UnicodeCategory.LetterNumber:
                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.ModifierLetter:
                    case UnicodeCategory.OtherLetter:
                    case UnicodeCategory.TitlecaseLetter:
                    case UnicodeCategory.UppercaseLetter:
                    case UnicodeCategory.Format:
                        break;
                    case UnicodeCategory.ClosePunctuation:
                    case UnicodeCategory.ConnectorPunctuation:
                    case UnicodeCategory.Control:
                    case UnicodeCategory.CurrencySymbol:
                    case UnicodeCategory.DashPunctuation:
                    case UnicodeCategory.EnclosingMark:
                    case UnicodeCategory.FinalQuotePunctuation:
                    case UnicodeCategory.InitialQuotePunctuation:
                    case UnicodeCategory.LineSeparator:
                    case UnicodeCategory.MathSymbol:
                    case UnicodeCategory.ModifierSymbol:
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.OpenPunctuation:
                    case UnicodeCategory.OtherNotAssigned:
                    case UnicodeCategory.OtherNumber:
                    case UnicodeCategory.OtherPunctuation:
                    case UnicodeCategory.OtherSymbol:
                    case UnicodeCategory.ParagraphSeparator:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.SpaceSeparator:
                    case UnicodeCategory.SpacingCombiningMark:
                    case UnicodeCategory.Surrogate:
                        asCharArray[i] = ' ';
                        break;
                    default:
                        asCharArray[i] = ' ';
                        break;
                }
            }

            identifier = new string(asCharArray);

            // Replace double spaces
            while (identifier.Contains("  "))
            {
                identifier = identifier.Replace("  ", " ");
            }

            identifier = string.Concat(identifier
                .Split(' ')
                .Where(element => !string.IsNullOrWhiteSpace(element))
                .Select((element, index) => index == 0
                    ? element
                    : element.Pascalize()));

            var leadingUnderscores = string.Empty;
            for (var i = 0; i < identifier.Length; i++)
            {
                if (identifier[i] == '_')
                {
                    continue;
                }

                leadingUnderscores = identifier[..i];
                identifier = identifier[i..];
                break;
            }

            if (!char.IsUpper(identifier[0]))
            {
                identifier = $"{char.ToUpperInvariant(identifier[0])}{identifier[1..]}";
            }

            identifier = $"{leadingUnderscores}{identifier}";

            if (char.IsNumber(identifier[0]))
            {
                identifier = $"_{identifier}";
            }

            return identifier;
        }


        private async Task UpdateReleaseNotesAsync(string directoy, string releaseVersion, NuGetDependencyConfig changedjsonFile, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(directoy, "release-notes.md");
            if (files.Length != 1)
            {
                Console.WriteLine($"Can't find `release-notes.md` in : {directoy}");
                return;
            }
            string releaseNotesFileName = files[0];
            var lines = await File.ReadAllLinesAsync(releaseNotesFileName, cancellationToken);

            StringBuilder sb = new StringBuilder();
            bool versionExists = false;

            for(int i = 0; i < lines.Length; i++)
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
                            sb.AppendLine("- Improvement: Updated NuGet packages to latest stables.");
                        }
                        else if (lines[i + 1] != "- Improvement: Updated NuGet packages to latest stables.")
                        {
                            sb.AppendLine("- Improvement: Updated NuGet packages to latest stables.");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"### Version {releaseVersion}");
                        sb.AppendLine();
                        sb.AppendLine("- Improvement: Updated NuGet packages to latest stables.");
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


            await File.WriteAllTextAsync(releaseNotesFileName, sb.ToString(), cancellationToken);
        }

        private async Task<string?> UpdateIModSpecAsync(string directoy, NuGetDependencyConfig changedjsonFile, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(directoy, "*.imodspec");
            if (files.Length != 1)
            {
                Console.WriteLine($"Can't find `imodspec` in : {directoy}");
                return null;
            }
            string imodspecFile = files[0];
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

            // Output the modified XML content with original formatting preserved
            using (var fileWriter = File.OpenWrite(imodspecFile))
            using (XmlWriter xmlWriter = XmlWriter.Create(fileWriter, new XmlWriterSettings { Indent = true, NewLineOnAttributes = false }))
            {
                doc.Save(xmlWriter);
            }
            var releaseVersion = new NuGetVersion(newVersion.Major, newVersion.Minor, newVersion.Patch);
            return releaseVersion.ToString();
        }

        private async Task PersistNugetPackageJsonFileAsync(NuGetDependencyConfig changedjsonFile, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Updating NuGet Dependencies : {changedjsonFile.Filename}");
            using (FileStream createStream = File.Create(changedjsonFile.Filename))
            {
                await JsonSerializer.SerializeAsync(createStream, changedjsonFile, _serialziationOptions, cancellationToken);
            }
        }

        /// <summary>
        /// Return true if package version info changed
        /// </summary>
        /// <param name="package"></param>
        /// <param name="versionInfo"></param>
        /// <returns></returns>
        private static bool UpdatePackageInfo(Package package, List<NugetVersionInfo> versionInfo)
        {
            var result = package.Versions.CompareCollections(versionInfo, (p, v) => p.Framework == v.GetFrameworkVersion());

            if (!result.HasChanges())
            {
                return false;
            }
            bool changed = false;
            foreach (var elementToAdd in result.ToAdd)
            {
                package.Versions.Add(new PackageVersion() { Framework = elementToAdd.GetFrameworkVersion(), Version = elementToAdd.PackageVersion.ToString() });
                changed = true;
            }

            foreach (var elementToRemove in result.ToRemove)
            {
                package.Versions.Remove(elementToRemove);
                changed = true;
            }

            foreach (var elementToEdit in result.PossibleEdits)
            {
                if (elementToEdit.Original.Locked != true && elementToEdit.Original.Version != elementToEdit.Changed.PackageVersion.ToString())
                {
                    elementToEdit.Original.Version = elementToEdit.Changed.PackageVersion.ToString();
                    changed = true;
                }
            }
            return changed;
        }

        private static void AddVerions(StringBuilder content, Package package)
        {
            for (var i = package.Versions.Count - 1; i >= 0; i--)
            {
                var current = package.Versions[i];
                if (i == 0)
                {
                    content.AppendLine($"                _ => \"{current.Version}\",");
                }
                else
                {
                    content.AppendLine($"                ({current.Framework.Replace(".", ", ")}) => \"{current.Version}\",");
                }
            }
        }
    }
}
