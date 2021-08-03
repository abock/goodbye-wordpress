// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;

namespace Goodbye.WordPress
{
    public sealed record Post(
        int Id,
        string? Status,
        DateTimeOffset? Date,
        string Name,
        string Title,
        string? Category,
        ImmutableList<string> Tags,
        string Content,
        string? OriginalUrl,
        ImmutableList<PostResource> Resources);
}
