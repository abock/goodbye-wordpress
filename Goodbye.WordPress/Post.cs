// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;

using Newtonsoft.Json;

namespace Goodbye.WordPress
{
    public sealed class Post
    {
        public int Id { get; }
        public string? Status { get; }
        public DateTimeOffset? Date { get; }
        public string Name { get; }
        public string Title { get; }
        public string? Category { get; }
        public ImmutableList<string> Tags { get; }
        public string Content { get; }
        public string? OriginalUrl { get; }
        public ImmutableList<PostResource> Resources { get; }

        [JsonConstructor]
        public Post(
            int id,
            string? status,
            DateTimeOffset? date,
            string name,
            string title,
            string? category,
            ImmutableList<string>? tags,
            string content,
            string? originalUrl,
            ImmutableList<PostResource>? resources = null)
        {
            Id = id;
            Status = status;
            Date = date;
            Name = name;
            Title = title;
            Category = category;
            Tags = tags ?? ImmutableList<string>.Empty;
            Content = content;
            OriginalUrl = originalUrl;
            Resources = resources ?? ImmutableList<PostResource>.Empty;
        }

        public Post WithContent(string content)
            => content == Content ? this : new Post(
                Id,
                Status,
                Date,
                Name,
                Title,
                Category,
                Tags,
                content,
                OriginalUrl,
                Resources);

        public Post WithResources(ImmutableList<PostResource> resources)
            => new Post(
                Id,
                Status,
                Date,
                Name,
                Title,
                Category,
                Tags,
                Content,
                OriginalUrl,
                resources);
    }
}
