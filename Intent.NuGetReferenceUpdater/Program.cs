using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Reflection;
using Humanizer;
using Intent.IArchitect.CrossPlatform.IO;

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
                Symbols.Arguments.UsernameArgument,
                Symbols.Arguments.PasswordArgument,
                Symbols.Arguments.IslnPathArgument,
                Symbols.Options.ResumeId,
            };

            rootCommand.SetHandler(
                handle: async (
                    string username,
                    string password,
                    FileSystemInfo? isln_path,
                    string? resumeId
                    ) =>
                {
                    if (isln_path == null)
                        throw new Exception($"{OptionName("islnName")} is required.");
                    var updater = new FileUpdater(rootCommand.Parse(args), username, password, isln_path.GetIslnFilePath());
                    await updater.UpdateFilesAsync(resumeId);
                },
                symbols: Enumerable.Empty<IValueDescriptor>()
                    .Concat(rootCommand.Arguments)
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
