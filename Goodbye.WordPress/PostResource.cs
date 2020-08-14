// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;

namespace Goodbye.WordPress
{
    public enum PostResourceDownloadStatus
    {
        NotAttempted,
        AlreadyDownloaded,
        Succeeded,
        Failed
    }

    public sealed class PostResource
    {
        public string OriginalUrl { get; }
        public string PostRelativePath { get; }
        public PostResourceDownloadStatus DownloadStatus { get; }
        public ImmutableList<Uri> AttemptedDownloadUris { get; }

        public PostResource(
            string originalUrl,
            string postRelativePath,
            PostResourceDownloadStatus downloadStatus = PostResourceDownloadStatus.NotAttempted,
            ImmutableList<Uri>? attemptedDownloadUris = null)
        {
            OriginalUrl = originalUrl;
            PostRelativePath = postRelativePath;
            DownloadStatus = downloadStatus;
            AttemptedDownloadUris = attemptedDownloadUris ?? ImmutableList<Uri>.Empty;
        }

        public PostResource WithDownloadStatus(
            PostResourceDownloadStatus downloadStatus,
            ImmutableList<Uri>? attemptedDownloadUris = null)
            => new PostResource(
                OriginalUrl,
                PostRelativePath,
                downloadStatus,
                attemptedDownloadUris);
    }
}
