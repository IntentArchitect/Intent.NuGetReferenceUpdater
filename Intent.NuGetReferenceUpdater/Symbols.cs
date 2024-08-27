using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.NuGetReferenceUpdater
{
    public static class Symbols
    {
        public static class Arguments
        {
            public static readonly Argument<string> UsernameArgument = new(
                name: "username",
                description: "Username for an active Intent Architect account.");

            public static readonly Argument<string> PasswordArgument = new(
                name: "password",
                description: "Password for the Intent Architect account.");

            public static readonly Argument<FileSystemInfo> IslnPathArgument = new(
                name: "isln-path",
                description: "Path to the Intent Architect solution (.isln) file or folder containing a single .isln file.");
        }

        public static class Options
        {
            public static readonly Option<string?> ApplicationIdOption = new(
                name: "--application-id",
                description: $"The Id of the Intent Architect application. If unspecified then all applications " +
                             "found in the .isln will be run.");

            public static readonly Option<bool?> AttachDebuggerOption = new(
                name: "--attach-debugger",
                description: "The Software Factory will pause at startup giving you chance to attach a .NET debugger.");

            public static readonly Option<string?> AccessToken = new(
                name: "--access-token",
                description: "A JWT access token to use instead of authenticating with the username and password.")
            {
                IsHidden = true
            };

            public static readonly Option<string?> StsBaseAddress = new(
                name: "--sts-base-address",
                description: "The base address for the STS from which to get an access token.")
            {
                IsHidden = true
            };

            public static readonly Option<bool?> SuppressVersioning = new(
                name: "--suppress-versioning",
                description: "Whether to suppress version bumping and release notes.");

            public static readonly Option<string?> ResumeId = new(
                name: "--resume-id",
                description: $"Resume processing from this application Id");


            static Options()
            {
                StsBaseAddress.SetDefaultValue("https://intentarchitect.com/");
            }
        }
    }
}
