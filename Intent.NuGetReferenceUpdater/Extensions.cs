using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.NuGetReferenceUpdater
{
    internal static class ExtensionMethods
    {
        public static string GetIslnFilePath(this ParseResult parseResult)
        {
            return parseResult.GetValueForArgument(Symbols.Arguments.IslnPathArgument).GetIslnFilePath();
        }

        public static string GetIslnFilePath(this FileSystemInfo islnPath)
        {
            switch (islnPath)
            {
                case FileInfo fileInfo:
                    return fileInfo.FullName;
                case DirectoryInfo directoryInfo:
                    var islnFiles = directoryInfo.GetFileSystemInfos("*.isln", new EnumerationOptions
                    {
                        MatchCasing = MatchCasing.CaseInsensitive,
                        RecurseSubdirectories = false
                    });

                    return islnFiles.Length switch
                    {
                        0 => throw new Exception($"No .isln files found in folder {islnPath.FullName}"),
                        1 => islnFiles[0].FullName,
                        _ => throw new Exception($"More than one .isln files found in folder {islnPath.FullName}")
                    };
                default:
                    throw new InvalidOperationException($"Unknown type: {islnPath?.GetType()}");
            }
        }
    }

}
