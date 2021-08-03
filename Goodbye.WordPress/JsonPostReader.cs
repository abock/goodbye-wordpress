// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Goodbye.WordPress
{
    public sealed class JsonPostReader : IPostReader
    {
        readonly string _jsonFile;

        public JsonPostReader(string jsonFile)
            => _jsonFile = jsonFile;

        public async IAsyncEnumerable<Post> ReadPostsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var posts = await JsonSerializer.DeserializeAsync<List<Post>>(
                File.OpenRead(_jsonFile),
                new JsonSerializerOptions
                {
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    }
                },
                cancellationToken)
                ?? new List<Post>();

            foreach (var post in posts)
            {
                await Task.Yield();
                yield return post;
            }
        }
    }
}
