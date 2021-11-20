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
        static readonly HashSet<int> s_supportedDatabaseVersions = new()
        {
            // https://codex.wordpress.org/WordPress_Versions
            38590, // 4.7 (Dec 6, 2016) through 4.9.15 (Jun 10, 2020)
            49752, // 5.7 (Mar 9, 2021) through 5.8.2 (Present)
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

            try
            {
                await connection.OpenAsync(cancellationToken);
            }
            catch (Exception e)
            {
                throw new ConnectionFailedException(e);
            }

            var dbSupported = false;
            var dbVersion = 0;

            try
            {
                dbSupported = await new MySqlCommand(
                    "SELECT option_value FROM wp_options WHERE option_name = 'db_version'",
                    connection).ExecuteScalarAsync(cancellationToken)
                    is string dbVersionString &&
                    int.TryParse(dbVersionString, out dbVersion) &&
                    s_supportedDatabaseVersions.Contains(dbVersion);
            }
            catch
            {
            }

            if (!dbSupported)
            {
                var e = new WordPressDbNotSupportedException(
                    s_supportedDatabaseVersions
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
                    connection).ExecuteScalarAsync(cancellationToken)
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
                    p.post_date AS PublishedLocalDate,
                    p.post_date_gmt AS PublishedUtcDate,
                    p.post_modified AS UpdatedLocalDate,
                    p.post_modified_gmt AS UpdatedUtcDate,
                    p.post_name AS Name,
                    p.post_title AS Title,
                    COALESCE(
                        GROUP_CONCAT(DISTINCT c.name SEPARATOR ';'), 
                        'NULL'
                    ) AS Category, 
                    COALESCE(
                        GROUP_CONCAT(DISTINCT t.name SEPARATOR ';'), 
                        'NULL'
                    ) AS Tags, 
                    p.post_content AS Content
                FROM wp_posts p
                LEFT JOIN wp_term_relationships cr
                    ON (p.id = cr.object_id)
                LEFT JOIN wp_term_taxonomy ct
                    ON (ct.term_taxonomy_id = cr.term_taxonomy_id
                        AND ct.taxonomy = 'category')
                LEFT JOIN wp_terms c
                    ON (ct.term_id = c.term_id)
                LEFT JOIN wp_term_relationships tr
                    ON (p.id = tr.object_id)
                LEFT JOIN wp_term_taxonomy tt
                    ON (tt.term_taxonomy_id = tr.term_taxonomy_id
                        AND tt.taxonomy = 'post_tag')
                LEFT JOIN wp_terms t
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

                var publishedDate = ParseDate(
                    reader.GetDateTime("PublishedLocalDate"),
                    reader.GetDateTime("PublishedUtcDate"));

                var updatedDate = ParseDate(
                    reader.GetDateTime("UpdatedLocalDate"),
                    reader.GetDateTime("UpdatedUtcDate"));

                if (publishedDate is null)
                    continue;

                var postName = reader.GetString("Name");

                var originalUrl = ExpandPermalinkStructure(
                    originalPermalinkStructure,
                    publishedDate.Value,
                    postId,
                    postName);

                yield return new Post(
                    postId,
                    reader.GetString("Status"),
                    publishedDate,
                    updatedDate,
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
                    originalUrl is null
                        ? ImmutableList<string>.Empty
                        : ImmutableList.Create(originalUrl),
                    ImmutableList<PostResource>.Empty);
            }
        }

        static DateTimeOffset? ParseDate(DateTime localDate, DateTime utcDate)
            => localDate > DateTime.MinValue && utcDate > DateTime.MinValue
                ? new DateTimeOffset(localDate, localDate - utcDate)
                : null;

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
                    return key.ToLowerInvariant() switch
                    {
                        "year" => $"{date:yyyy}",
                        "monthnum" => $"{date:MM}",
                        "day" => $"{date:dd}",
                        "hour" => $"{date:HH}",
                        "minute" => $"{date:mm}",
                        "second" => $"{date:ss}",
                        "post_id" => id.ToString(CultureInfo.InvariantCulture),
                        "postname" => name,
                        _ => key,
                    };
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
