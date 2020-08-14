// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Threading.Tasks;

using Mono.Options;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Goodbye.WordPress
{
    public static class Program
    {
        static int verbosity = 1;
        static bool showHelp;

        static ConnectionStringBuilder connectionString = new ConnectionStringBuilder();
        static bool ignoreUnsupportedDatabaseVersions;
        static WordPressExporter exporter = WordPressExporter.Create();

        static readonly OptionSet options = new OptionSet
        {
            { "usage: goodbye-wordpress [OPTIONS] [JSON_INPUT_FILE]" },
            { "" },
            { "Options:" },
            { "" },
            { "?|help", "Show this help",
                v => showHelp = v != null },
            { "v|verbose", "Use verbose logging",
                v => verbosity += v is null ? -1 : 1 },
            { "q|quiet", "Use quiet logging (errors only); synonym for -v-",
                v => verbosity = 0 },
            { "" },
            { "Post Options:" },
            { "" },
            { "o|output-dir=", "Set the output directory for posts and images",
                v => exporter = exporter.WithContentOutputDirectory(v) },
            { "format=", "Output {FORMAT}: markdown | html | raw",
                v => exporter = exporter.WithOutputFormat(Enum.Parse<OutputFormat>(v, true)) },
            { "serialize-json=", "Serialize the entire post set to {FILE}",
                v => exporter = exporter.WithArchiveOutputFilePath(v) },
            { "" },
            { "MySQL Options:" },
            { "" },
            { "h|host=", "Connect to host",
                v => connectionString.Host = v },
            { "P|port=", "Connect through port",
                v => connectionString.Port = int.Parse(v, CultureInfo.InvariantCulture) },
            { "u|user=", "User for login",
                v => connectionString.Username = v },
            { "p|password=", "Password to use",
                v => connectionString.Password = v },
            { "D|database=", "Database to use",
                v => connectionString.Database = v },
            { "i|ignore-unsupported-db-versions", "Ignore unsupported WordPress database versions",
                v => ignoreUnsupportedDatabaseVersions = v != null }
        };

        static int ShowHelp(string? error = null, Exception? exception = null)
        {
            if (error != null || exception != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Error.WriteLine($"error: {error ?? exception?.Message}");
                Console.ResetColor();
                if (verbosity > 0)
                    Console.Error.WriteLine(exception);
                Console.Error.WriteLine();
            }

            options.WriteOptionDescriptions(Console.Error);
            return 1;
        }

        static async Task<int> Main(string[] args)
        {
            IPostReader? postReader = null;

            try
            {
                var positionalArgs = options.Parse(args);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Is(Math.Clamp(verbosity, 0, 3) switch
                    {
                        1 => LogEventLevel.Information,
                        2 => LogEventLevel.Debug,
                        3 => LogEventLevel.Verbose,
                        _ => LogEventLevel.Warning
                    })
                    .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                    .CreateLogger();

                if (showHelp)
                    return ShowHelp();

                if (connectionString.IsConfigured)
                {
                    var mysqlPostReader = new MysqlPostReader(
                        connectionString,
                        ignoreUnsupportedDatabaseVersions);
                    postReader = mysqlPostReader;

                    Log.Debug(
                        "MySQL Connection String: {ConnectionString}",
                        mysqlPostReader.ConnectionString.BuildConnectionString(true));
                }

                if (positionalArgs.Count > 0)
                {
                    if (postReader is object)
                        return ShowHelp("cannot export from both JSON archive and a MySQL database");

                    postReader = new JsonPostReader(positionalArgs[0]);
                }

                if (postReader is null)
                    return ShowHelp("no WordPress MySQL connection options or JSON input file provided");
            }
            catch (Exception e)
            {
                return ShowHelp(exception: e);
            }

            exporter = await exporter
                .WithPostReader(postReader)
                .ExportAsync();

            return 0;
        }
    }
}
