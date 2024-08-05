using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.IO;
using System.Reflection;
using Humanizer;

namespace Intent.NuGetReferenceUpdater
{
    internal class Program
    {
        private static string OptionName(string propertyName) => $"--{propertyName.Kebaberize()}";

        static async Task Main(string[] args)
        {
            var rootCommand = new RootCommand(
                @"The Intent NuGet package updater.")
            {
                new Option<string>(
                    name: OptionName("Directory"),
                    description: "Directory to scan for NugetPacakges.json"),
                new Option<bool>(
                    name: OptionName("ForceCodeUpdates"),
                    description: "Update module files NugetPacakes.cs even if no new package versions"),
                new Option<bool>(
                    name: OptionName("SuppressVersioning"),
                    description: "Don't update .modspec and release notes"),
                new Option<bool>(
                    name: OptionName("SkipNugetCheck"),
                    description: "Don't check NuGet for new versions"),
            };

            rootCommand.SetHandler(
                handle: async (
                    string? directory,
                    bool forceCodeUpdates,
                    bool suppressVersioning,
                    bool skipNugetCheck
                    ) =>
                {
                    if (directory == null)
                        throw new Exception($"{OptionName("Directory")} is required.");
                    var updater = new FileUpdater(forceCodeUpdates == true, suppressVersioning == true, skipNugetCheck == true);
                    await updater.UpdateFilesAsync(directory);
                },
                symbols: Enumerable.Empty<IValueDescriptor>()
                    .Concat(rootCommand.Options)
                    .ToArray());

            Console.WriteLine($"{rootCommand.Name} version {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

            new CommandLineBuilder(rootCommand)
                .UseDefaults()
                .Build()
                .Invoke(args);
        }
    }
}
