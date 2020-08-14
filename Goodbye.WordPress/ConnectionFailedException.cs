// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;

namespace Goodbye.WordPress
{
    public sealed class ConnectionFailedException : Exception
    {
        internal ConnectionFailedException(Exception innerException)
            : base(
                innerException.Message,
                innerException)
        {
        }
    }
}
