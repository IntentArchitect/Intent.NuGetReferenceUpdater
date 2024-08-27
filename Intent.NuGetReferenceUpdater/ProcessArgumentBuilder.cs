using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Intent.NuGetReferenceUpdater
{
    public class ProcessArgumentBuilder
    {
        private readonly ParseResult _parseResult;
        private readonly List<string> _arguments = new();

        public ProcessArgumentBuilder(ParseResult parseResult)
        {
            _parseResult = parseResult;
        }

        public ProcessArgumentBuilder WithArgument(string argument)
        {
            if (argument.Contains(' ') || argument == string.Empty)
            {
                argument = $"\"{argument}\"";
            }

            _arguments.Add(argument);

            return this;
        }

        public ProcessArgumentBuilder WithArgument<T>(Argument<T> argument, string? value = null)
        {
            return WithArgument(value ?? _parseResult.GetValueForArgument(argument)!.ToString()!);
        }

        public ProcessArgumentBuilder WithOption<T>(Option<T> option, string? value = null)
        {
            value ??= _parseResult.GetValueForOption(option)?.ToString();
            if (value == null)
            {
                return this;
            }

            return WithArgument(option.Aliases.First())
                .WithArgument(value);
        }

        public string Build()
        {
            return string.Join(' ', _arguments);
        }
    }
}