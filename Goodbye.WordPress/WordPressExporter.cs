// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

namespace Goodbye.WordPress
{
    public enum OutputFormat
    {
        None,
        Raw,
        Html,
        Markdown,
    }

    public sealed record WordPressExporter
    {
        sealed class NullPostReader : IPostReader
        {
            public static readonly NullPostReader Instance = new();

            public async IAsyncEnumerable<Post> ReadPostsAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield break;
            }
        }

        public IPostReader PostReader { get; init; }
        public OutputFormat OutputFormat { get; init; }
        public Uri? BaseUri { get; init; }
        public string ContentOutputDirectory { get; init; }
        public string ImagesOutputDirectory { get; init; }
        public string? ArchiveOutputFilePath { get; init; }
        public WordPressExporterDelegate Delegate { get; init; }
        public ImmutableList<(Post Original, Post Processed)> Posts { get; init; }

        WordPressExporter(
            IPostReader? postReader,
            OutputFormat outputFormat,
            Uri? baseUri,
            string contentOutputDirectory,
            string? imagesOutputDirectory,
            string? archiveOutputFilePath,
            WordPressExporterDelegate? @delegate,
            ImmutableList<(Post, Post)>? posts)
        {
            PostReader = postReader ?? NullPostReader.Instance;
            OutputFormat = outputFormat;
            BaseUri = baseUri;
            ContentOutputDirectory = contentOutputDirectory;
            ImagesOutputDirectory = imagesOutputDirectory
                ?? Path.Combine(contentOutputDirectory, "images");
            ArchiveOutputFilePath = archiveOutputFilePath;
            Delegate = @delegate ?? new WordPressExporterDelegate();
            Posts = posts ?? ImmutableList<(Post, Post)>.Empty;
        }

        public static WordPressExporter Create(
            IPostReader? postReader = null,
            OutputFormat outputFormat = OutputFormat.Markdown,
            Uri? baseUri = null,
            string? contentOutputDirectory = null,
            string? archiveOutputFilePath = null,
            WordPressExporterDelegate? @delegate = null)
            => new(
                postReader,
                outputFormat,
                baseUri,
                contentOutputDirectory
                    ?? Path.Combine(Environment.CurrentDirectory, "posts"),
                null,
                archiveOutputFilePath,
                @delegate,
                null);

        public async Task<WordPressExporter> ExportAsync(
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(ContentOutputDirectory);
            Directory.CreateDirectory(ImagesOutputDirectory);

            var posts = ImmutableList.CreateBuilder<(Post, Post)>();
            var resources = ImmutableList.CreateBuilder<PostResource>();

            await foreach (var originalPost in PostReader.ReadPostsAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var processedPost = Delegate.ProcessPost(this, originalPost);

                var outputPath = Path.ChangeExtension(
                    Delegate.GetOutputPath(this, processedPost),
                    Delegate.GetFileExtension(this));

                using var writer = Delegate.GetStreamWriter(
                    this,
                    processedPost,
                    outputPath);

                Delegate.WritePost(
                    this,
                    processedPost,
                    writer,
                    writeYamlFrontMatter: true);

                Log.Information("Post {Title} ({Date}) → {Path}",
                    processedPost.Title,
                    processedPost.Published,
                    Path.GetRelativePath(
                        Environment.CurrentDirectory,
                        outputPath));

                var updatedResources = ImmutableList.CreateBuilder<PostResource>();

                foreach (var resource in processedPost.Resources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    updatedResources.Add(
                        await Delegate.DownloadResourceAsync(
                            this,
                            processedPost,
                            resource,
                            cancellationToken));
                }

                processedPost = processedPost with
                {
                    Resources = updatedResources.ToImmutable()
                };

                posts.Add((
                    originalPost with
                    {
                        Resources = processedPost.Resources,
                    },
                    processedPost));
            }

            var exporter = new WordPressExporter(
                PostReader,
                OutputFormat,
                BaseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                Delegate,
                posts.ToImmutable());

            Delegate.WriteArchive(exporter);

            return exporter;
        }
    }
}
