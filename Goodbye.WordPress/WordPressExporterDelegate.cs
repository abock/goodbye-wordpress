// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using Serilog.Events;

using SharpYaml.Serialization;

namespace Goodbye.WordPress
{
    public class WordPressExporterDelegate
    {
        public static Encoding Utf8NoBomEncoding { get; } = new UTF8Encoding(false);

        protected virtual HttpClient HttpClient { get; } = new(
            new HttpClientHandler
            {
                AllowAutoRedirect = true
            });

        protected virtual JsonSerializerOptions JsonSerializerOptions { get; } = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull | JsonIgnoreCondition.WhenWritingDefault,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        public virtual string GetFileExtension(WordPressExporter exporter)
            => exporter.OutputFormat == OutputFormat.Markdown
                ? ".md"
                : ".html";

        public virtual string GetOutputPath(
            WordPressExporter exporter,
            Post post)
            => Path.Combine(
                exporter.ContentOutputDirectory,
                $"{post.Published:yyyy-MM-dd}-{post.Name}");

        public virtual StreamWriter GetStreamWriter(
            WordPressExporter exporter,
            Post post,
            string path)
            => new(
                path,
                false,
                Utf8NoBomEncoding)
                {
                    NewLine = "\n"
                };

        public virtual void WriteArchive(
            WordPressExporter exporter,
            Func<(Post OriginalPost, Post ProcessedPost), Post>? postSelector = null)
        {
            if (string.IsNullOrEmpty(exporter.ArchiveOutputFilePath))
                return;

            var dir = Path.GetDirectoryName(exporter.ArchiveOutputFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(
                exporter.ArchiveOutputFilePath,
                JsonSerializer.Serialize(
                    exporter.Posts.Select(post => WriteArchivePostSelector(exporter, post)),
                    JsonSerializerOptions),
                Utf8NoBomEncoding);
        }

        public virtual Post WriteArchivePostSelector(
            WordPressExporter exporter,
            (Post OriginalPost, Post ProcessedPost) post)
            => post.OriginalPost;

        public virtual Post ProcessPost(
            WordPressExporter exporter,
            Post post)
        {
            post = RewriteImageSources(exporter, post);

            if (exporter.OutputFormat != OutputFormat.Raw)
            {
                post = AddMissingWordPressHtml(exporter, post);
                if (exporter.OutputFormat == OutputFormat.Markdown)
                    post = ConvertToMarkdown(exporter, post);
            }

            return post;
        }

        public virtual Post RewriteImageSources(
            WordPressExporter exporter,
            Post post)
        {
            var resources = new List<PostResource>();

            post = post with { Content = Regex.Replace(
                post.Content,
                @"(<img src=['""]?)([^'"">]+)",
                match =>
                {
                    var src = match.Groups["2"].Value.Trim();

                    var fullPath = Path.Combine(
                        exporter.ImagesOutputDirectory,
                        Path.GetFileName(src));

                    var relPath = Path.GetRelativePath(
                        exporter.ContentOutputDirectory,
                        fullPath);

                    resources.Add(new PostResource(src, relPath));

                    return match.Groups["1"].Value + relPath;
                }) };

            if (resources.Count > 0)
            {
                var updatedResources = post.Resources;
                foreach (var newResource in resources)
                    updatedResources = updatedResources
                        .RemoveAll(old => old.OriginalUrl == newResource.OriginalUrl)
                        .Add(newResource);
                post = post with { Resources =  updatedResources };
            }

            return post;
        }

        public virtual Post AddMissingWordPressHtml(
            WordPressExporter exporter,
            Post post)
        {
            var html = new StringBuilder();
            var inPre = false;

            foreach (var line in post.Content.Split("\n").Select(line => line.Trim()))
            {
                if (line.StartsWith("<pre", StringComparison.OrdinalIgnoreCase))
                    inPre = true;

                if (string.IsNullOrEmpty(line))
                    html.AppendLine();
                else if (!inPre && !Regex.IsMatch(
                    line,
                    "^<(p|div|ul|ol|li|blockquote|h{1,6}|dl|dd|hr)",
                    RegexOptions.IgnoreCase))
                    html.Append("<p>").Append(line).AppendLine("</p>");
                else
                    html.AppendLine(line);

                if (line.EndsWith("</pre>", StringComparison.OrdinalIgnoreCase))
                    inPre = false;
            }

            return post with { Content = html.ToString().Trim() };
        }

        public virtual Post ConvertToMarkdown(
            WordPressExporter exporter,
            Post post)
            => post with { Content = new ReverseMarkdown.Converter()
                .Convert(post.Content)
                .Trim() };

        public virtual void WritePost(
            WordPressExporter exporter,
            Post post,
            TextWriter writer,
            bool writeYamlFrontMatter)
        {
            if (writeYamlFrontMatter)
                WritePostYamlFrontMatter(exporter, post, writer);

            writer.WriteLine(post.Content);

            writer.Flush();
        }

        public virtual string YamlFrontMatterOpeningDelimeter { get; } = "---";
        public virtual string YamlFrontMatterClosingDelimeter { get; } = "---";
        public virtual bool YamlFrontMatterBlankLineAfterClosingDelimeter { get; } = true;

        public virtual void WritePostYamlFrontMatter(
            WordPressExporter exporter,
            Post post,
            TextWriter writer)
        {
            writer.WriteLine(YamlFrontMatterOpeningDelimeter);

            var rootNode = new YamlMappingNode();

            PopulatePostYamlFrontMatter(
                exporter,
                post,
                rootNode);

            new YamlStream(new YamlDocument(rootNode))
                .Save(
                    writer,
                    isLastDocumentEndImplicit: true);

            writer.WriteLine(YamlFrontMatterClosingDelimeter);
            if (YamlFrontMatterBlankLineAfterClosingDelimeter)
                writer.WriteLine();
        }

        public virtual void PopulatePostYamlFrontMatter(
            WordPressExporter exporter,
            Post post,
            YamlMappingNode rootNode)
        {
            void AddDateField(string name, DateTimeOffset? date)
            {
                if (date.HasValue)
                    rootNode.Add(name, date.Value.ToString(
                        "yyyy-MM-dd HH:mm:ss zzz",
                        CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(post.Title))
                rootNode.Add(nameof(Post.Title), post.Title);

            AddDateField(nameof(Post.Published), post.Published);
            AddDateField(nameof(Post.Updated), post.Updated);

            if (!string.IsNullOrEmpty(post.Category))
                rootNode.Add(
                    nameof(Post.Category),
                    post.Category);

            if (post.Tags.Count > 0)
                rootNode.Add(
                    nameof(Post.Tags),
                    new YamlSequenceNode(
                        post.Tags.Select(tag => new YamlScalarNode(tag))));

            if (post.Status != "publish")
                rootNode.Add("Excluded", "true");

            if (!string.IsNullOrEmpty(post.Status))
                rootNode.Add(
                    nameof(Post.Status),
                    post.Status);

            if (post.RedirectFrom.Count > 0)
            {
                var redirects = post.RedirectFrom
                    .Select(RewriteRedirectFrom)
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Select(r => new YamlScalarNode(r))
                    .ToList();

                if (redirects.Count > 0)
                    // https://statiq.dev/web/content-and-data/content/redirects
                    rootNode.Add(
                        nameof(Post.RedirectFrom),
                        new YamlSequenceNode(redirects));
            }
        }

        public virtual string? RewriteRedirectFrom(string? originalUrl)
        {
            if (string.IsNullOrEmpty(originalUrl))
                return null;

            return originalUrl.TrimStart('/');
        }

        public virtual IEnumerable<Uri> GetDownloadResourceUris(
            WordPressExporter exporter,
            Post post,
            PostResource resource)
        {
            Uri GetPrimary()
            {
                var uri = resource.OriginalUrl;
                if (uri.StartsWith("//", StringComparison.Ordinal))
                    uri = "https:" + uri;

                if (exporter.BaseUri is null)
                    return new Uri(uri);

                return new Uri(exporter.BaseUri, resource.OriginalUrl);
            }

            yield return GetPrimary();
        }

        public virtual async Task<PostResource> DownloadResourceAsync(
            WordPressExporter exporter,
            Post post,
            PostResource resource,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(
                Path.Combine(
                    exporter.ContentOutputDirectory,
                    resource.PostRelativePath));

            var uris = GetDownloadResourceUris(
                exporter,
                post,
                resource).ToImmutableList();

            if (File.Exists(fullPath))
                return resource.WithDownloadStatus(
                    PostResourceDownloadStatus.AlreadyDownloaded,
                    uris);

            var status = PostResourceDownloadStatus.Failed;
            var messages = new List<(
                Uri Uri,
                Exception? Exception,
                HttpStatusCode? StatusCode,
                string? ReasonPhrase
            )>();

            foreach (var uri in uris)
            {
                if (uri is null)
                    continue;

                try
                {
                    Log.Information(
                        "  Resource {Uri} → {Path}",
                        uri,
                        Path.GetRelativePath(
                            Environment.CurrentDirectory,
                            fullPath));

                    using var httpResponse = await HttpClient.GetAsync(
                        uri,
                        cancellationToken);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        messages.Add((
                            uri,
                            null,
                            httpResponse.StatusCode,
                            httpResponse.ReasonPhrase));
                        continue;
                    }

                    using var httpStream = await httpResponse
                        .Content
                        .ReadAsStreamAsync(cancellationToken);

                    using var fileStream = File.OpenWrite(fullPath);

                    await httpStream.CopyToAsync(
                        fileStream,
                        cancellationToken);

                    status = PostResourceDownloadStatus.Succeeded;
                    break;
                }
                catch (Exception e)
                {
                    messages.Add((uri, e, null, null));
                }
            }

            var indent = Log.IsEnabled(LogEventLevel.Information)
                ? "    "
                : "";

            foreach (var message in messages)
            {
                if (message.Exception is null)
                    Log.Error(
                        indent + "Failed to download {Uri}: {StatusCode} {ReasonPhrase}",
                        message.Uri,
                        (int?)message.StatusCode,
                        message.ReasonPhrase);
                else
                    Log.Error(
                        message.Exception,
                        indent + "Failed to download {Uri}",
                        message.Uri);
            }

            return resource.WithDownloadStatus(status, uris);
        }
    }
}
