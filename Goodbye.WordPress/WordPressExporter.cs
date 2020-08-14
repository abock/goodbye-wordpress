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

    public sealed class WordPressExporter
    {
        sealed class NullPostReader : IPostReader
        {
            public static readonly NullPostReader Instance = new NullPostReader();

            public async IAsyncEnumerable<Post> ReadPostsAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield break;
            }
        }

        public IPostReader PostReader { get; }
        public OutputFormat OutputFormat { get; }
        public Uri? BaseUri { get; }
        public string ContentOutputDirectory { get; }
        public string ImagesOutputDirectory { get; }
        public string? ArchiveOutputFilePath { get; }
        public WordPressExporterDelegate Delegate { get; }

        readonly ImmutableList<(Post, Post)> posts;
        public IReadOnlyList<(Post Original, Post Processed)> Posts => posts;

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

            this.posts = posts ?? ImmutableList<(Post, Post)>.Empty;
        }

        public static WordPressExporter Create(
            IPostReader? postReader = null,
            OutputFormat outputFormat = OutputFormat.Markdown,
            Uri? baseUri = null,
            string? contentOutputDirectory = null,
            string? archiveOutputFilePath = null,
            WordPressExporterDelegate? @delegate = null)
            => new WordPressExporter(
                postReader,
                outputFormat,
                baseUri,
                contentOutputDirectory
                    ?? Path.Combine(Environment.CurrentDirectory, "posts"),
                null,
                archiveOutputFilePath,
                @delegate,
                null);

        public WordPressExporter WithPostReader(IPostReader? postReader)
            => new WordPressExporter(
                postReader,
                OutputFormat,
                BaseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                Delegate,
                posts);

        public WordPressExporter WithOutputFormat(OutputFormat outputFormat)
            => new WordPressExporter(
                PostReader,
                outputFormat,
                BaseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                Delegate,
                posts);

        public WordPressExporter WithBaseUri(Uri? baseUri)
            => new WordPressExporter(
                PostReader,
                OutputFormat,
                baseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                Delegate,
                posts);

        public WordPressExporter WithContentOutputDirectory(string outputDirectory)
            => new WordPressExporter(
                PostReader,
                OutputFormat,
                BaseUri,
                outputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                Delegate,
                posts);

        public WordPressExporter WithArchiveOutputFilePath(string filePath)
            => new WordPressExporter(
                PostReader,
                OutputFormat,
                BaseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                filePath,
                Delegate,
                posts);

        public WordPressExporter WithDelegate(WordPressExporterDelegate @delegate)
            => new WordPressExporter(
                PostReader,
                OutputFormat,
                BaseUri,
                ContentOutputDirectory,
                ImagesOutputDirectory,
                ArchiveOutputFilePath,
                @delegate,
                posts);

        public async Task<WordPressExporter> ExportAsync(
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(ContentOutputDirectory);
            Directory.CreateDirectory(ImagesOutputDirectory);

            var posts = ImmutableList.CreateBuilder<(Post, Post)>();
            var resources = ImmutableList.CreateBuilder<PostResource>();

            await foreach (var originalPost in PostReader.ReadPostsAsync())
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
                    processedPost.Date,
                    Path.GetRelativePath(
                        Environment.CurrentDirectory,
                        outputPath));

                var updatedResources = ImmutableList.CreateBuilder<PostResource>();

                foreach (var resource in processedPost.Resources)
                    updatedResources.Add(
                        await Delegate.DownloadResourceAsync(
                            this,
                            processedPost,
                            resource));

                processedPost = processedPost.WithResources(updatedResources.ToImmutable());

                posts.Add((
                    originalPost.WithResources(processedPost.Resources),
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
