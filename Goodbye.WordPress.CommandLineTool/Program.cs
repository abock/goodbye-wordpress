// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Reflection;

using Mono.Options;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

using Goodbye.WordPress;

var version = Assembly.GetEntryAssembly()
    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0";

var copyright = Assembly.GetEntryAssembly()
    ?.GetCustomAttribute<AssemblyCopyrightAttribute>()
    ?.Copyright ?? "";

var verbosity = 1;
var showHelp = false;
var showVersion = false;

var connectionString = new ConnectionStringBuilder();
var ignoreUnsupportedDatabaseVersions = false;
var exporter = WordPressExporter.Create();

var options = new OptionSet
{
    { $"Goodbye Wordpress v{version}" },
    { copyright },
    { "https://github.com/abock/goodbye-wordpress" },
    { "" },
    { "usage: goodbye-wordpress [OPTIONS] [JSON_INPUT_FILE]" },
    { "" },
    { "Options:" },
    { "" },
    { "?|help", "Show this help",
        v => showHelp = v != null },
    { "version", "Show version",
        v => showVersion = v != null },
    { "v|verbose", "Use verbose logging",
        v => verbosity += v is null ? -1 : 1 },
    { "q|quiet", "Use quiet logging (errors only); synonym for -v-",
        v => verbosity = 0 },
    { "" },
    { "Post Options:" },
    { "" },
    { "o|output-dir=", "Set the output directory for posts and images",
        v => exporter = exporter with { ContentOutputDirectory = v } },
    { "format=", "Output {FORMAT}: markdown | html | raw",
        v => exporter = exporter with { OutputFormat = Enum.Parse<OutputFormat>(v, true) } },
    { "serialize-json=", "Serialize the entire post set to {FILE}",
        v => exporter = exporter with { ArchiveOutputFilePath = v } },
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

int ShowHelp(string? error = null, Exception? exception = null)
{
    if (error != null || exception != null)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine($"error: {error ?? exception?.Message}");
        Console.ResetColor();
        if (verbosity > 1 && exception != null)
            Console.Error.WriteLine(exception);
        Console.Error.WriteLine();
    }

    options.WriteOptionDescriptions(Console.Error);
    return 1;
}

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
    
    if (showVersion)
    {
        Console.WriteLine(version);
        return 1;
    }

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

try
{
    exporter = await (exporter with { PostReader = postReader }).ExportAsync();
}
catch (ConnectionFailedException e)
{
    Log.Fatal(
        verbosity > 1 ? e : null,
        "Cannot connect to WordPress: {Message}",
        e.Message);
}
catch (Exception e)
{
    Log.Fatal(
        e,
        "Failed to perform export process for an unknown reason: {Message}",
        e.Message);
}

return 0;
