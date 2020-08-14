// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;

using MySqlConnector;

using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Goodbye.WordPress
{
    public sealed class MysqlPostReader : IPostReader
    {
        static readonly HashSet<int> supportedDatabaseVersions = new HashSet<int>
        {
            // https://codex.wordpress.org/WordPress_Versions
            38590, // 4.7 (December 6, 2016) through 4.9.15 (June 10, 2020)
        };

        public ConnectionStringBuilder ConnectionString { get; }
        public bool IgnoreUnsupportedDatabaseVersion { get; }

        public MysqlPostReader(
            ConnectionStringBuilder connectionString,
            bool ignoreUnsupportedDatabaseVersion = false)
        {
            ConnectionString = new ConnectionStringBuilder(
                connectionString.Parameters.Add(("ConvertZeroDateTime", "true")));
            IgnoreUnsupportedDatabaseVersion = ignoreUnsupportedDatabaseVersion;
        }

        public async IAsyncEnumerable<Post> ReadPostsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string? originalPermalinkStructure = null;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);

            bool dbSupported = false;
            int dbVersion = 0;

            try
            {
                dbSupported = await new MySqlCommand(
                    "SELECT option_value FROM wp_options WHERE option_name = 'db_version'",
                    connection).ExecuteScalarAsync()
                    is string dbVersionString &&
                    int.TryParse(dbVersionString, out dbVersion) &&
                    supportedDatabaseVersions.Contains(dbVersion);
            }
            catch
            {
            }

            if (!dbSupported)
            {
                var e = new WordPressDbNotSupportedException(
                    supportedDatabaseVersions
                        .OrderByDescending(v => v)
                        .ToArray(),
                    dbVersion);
                
                if (IgnoreUnsupportedDatabaseVersion)
                    Log.Write(e.LogEvent);
                else
                    throw e;
            }

            try
            {
                if (await new MySqlCommand(
                    "SELECT option_value FROM wp_options WHERE option_name = 'permalink_structure'",
                    connection).ExecuteScalarAsync()
                    is string permalinkStructure &&
                    permalinkStructure.Length > 0)
                    originalPermalinkStructure = permalinkStructure;
            }
            catch
            {
            }

            var command = new MySqlCommand(@"
                SELECT
                    p.id AS ID,
                    p.post_status AS Status,
                    p.post_date AS LocalDate,
                    p.post_date_gmt AS UtcDate,
                    p.post_name AS Name,
                    p.post_title AS Title,
                    c.name AS Category,
                    GROUP_CONCAT(
                        DISTINCT t.name
                        SEPARATOR ';') AS Tags,
                    p.post_content AS Content
                FROM wp_posts p
                JOIN wp_term_relationships cr
                    ON (p.id = cr.object_id)
                JOIN wp_term_taxonomy ct
                    ON (ct.term_taxonomy_id = cr.term_taxonomy_id
                        AND ct.taxonomy = 'category')
                JOIN wp_terms c
                    ON (ct.term_id = c.term_id)
                JOIN wp_term_relationships tr
                    ON (p.id = tr.object_id)
                JOIN wp_term_taxonomy tt
                    ON (tt.term_taxonomy_id = tr.term_taxonomy_id
                        AND tt.taxonomy = 'post_tag')
                JOIN wp_terms t
                    ON (tt.term_id = t.term_id)
                WHERE
                    p.post_type = 'post'
                GROUP BY p.id
            ", connection);

            var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var postId = reader.GetInt32("ID");

                var localDate = reader.GetDateTime("LocalDate");
                var utcDate = reader.GetDateTime("UtcDate");
                var date = localDate > DateTime.MinValue && utcDate> DateTime.MinValue
                    ? new DateTimeOffset(localDate, localDate - utcDate)
                    : (DateTimeOffset?)null;

                if (date is null)
                    continue;

                var postName = reader.GetString("Name");

                yield return new Post(
                    postId,
                    reader.GetString("Status"),
                    date,
                    postName,
                    reader.GetString("Title"),
                    reader.GetString("Category") is string category &&
                        !string.Equals(
                            category,
                            "uncategorized",
                            StringComparison.OrdinalIgnoreCase)
                        ? category
                        : null,
                    reader.GetString("Tags")
                        .Split(";")
                        .Select(t => t.Trim().ToLowerInvariant())
                        .Distinct()
                        .ToImmutableList(),
                    reader.GetString("Content"),
                    ExpandPermalinkStructure(
                        originalPermalinkStructure,
                        date.Value,
                        postId,
                        postName));
            }
        }

        static string? ExpandPermalinkStructure(
            string? originalPermalinkStructure,
            DateTimeOffset date,
            int id,
            string name)
        {
            if (string.IsNullOrEmpty(originalPermalinkStructure))
                return null;

            return Regex.Replace(
                originalPermalinkStructure,
                "%(?<key>[^%]+)%",
                match =>
                {
                    var key = match.Groups["key"].Value;
                    switch (key.ToLowerInvariant())
                    {
                        case "year":
                            return $"{date:yyyy}";
                        case "monthnum":
                            return $"{date:MM}";
                        case "day":
                            return $"{date:dd}";
                        case "hour":
                            return $"{date:HH}";
                        case "minute":
                            return $"{date:mm}";
                        case "second":
                            return $"{date:ss}";
                        case "post_id":
                            return id.ToString(CultureInfo.InvariantCulture);
                        case "postname":
                            return name;
                        default:
                            return key;
                    }
                });
        }
    }

    public sealed class WordPressDbNotSupportedException : Exception
    {
        public IReadOnlyList<int> SupportedVersions { get; }
        public int EncounteredVersion { get; }
        public LogEvent LogEvent { get; }

        WordPressDbNotSupportedException(
            IReadOnlyList<int> supportedVersions,
            int encounteredVersion,
            LogEvent logEvent)
            : base(logEvent.RenderMessage())
        {
            SupportedVersions = supportedVersions;
            EncounteredVersion = encounteredVersion;
            LogEvent = logEvent;
        }

        internal WordPressDbNotSupportedException(
            IReadOnlyList<int> supportedVersions,
            int encounteredVersion)
            : this(
                supportedVersions,
                encounteredVersion,
                new LogEvent(
                    DateTimeOffset.Now,
                    LogEventLevel.Warning,
                    null,
                    new MessageTemplateParser().Parse(
                        "Unsupported datbase version {EncounteredVersion}; supported versions: {SupportedVersions}. " +
                        "See https://codex.wordpress.org/WordPress_Versions for a list of all database versions. " +
                        "Add your version to MysqlPostReader.supportedDatabaseVersions and if things work, " +
                        "submit a pull request please!"),
                    new[]
                    {
                        new LogEventProperty(
                            "EncounteredVersion",
                            new ScalarValue(encounteredVersion)),
                        new LogEventProperty(
                            "SupportedVersions",
                            new SequenceValue(
                                supportedVersions.Select(
                                    v => new ScalarValue(v))))
                    }))
        {
        }
    }
}
