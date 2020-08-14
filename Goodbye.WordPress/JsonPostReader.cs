// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace Goodbye.WordPress
{
    public sealed class JsonPostReader : IPostReader
    {
        readonly string jsonFile;

        public JsonPostReader(string jsonFile)
        {
            this.jsonFile = jsonFile;
        }

        public async IAsyncEnumerable<Post> ReadPostsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var serializer = JsonSerializer.CreateDefault();
            var reader = new StreamReader(File.OpenRead(jsonFile));
            var posts = serializer.Deserialize<List<Post>>(new JsonTextReader(reader))
                ?? new List<Post>();

            foreach (var post in posts)
            {
                await Task.Yield();
                yield return post;
            }
        }
    }
}
