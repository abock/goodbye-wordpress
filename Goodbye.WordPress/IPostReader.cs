// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;

namespace Goodbye.WordPress
{
    public interface IPostReader
    {
        IAsyncEnumerable<Post> ReadPostsAsync(CancellationToken cancellationToken = default);
    }
}
