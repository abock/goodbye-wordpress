// https://github.com/abock/goodbye-wordpress
// Copyright 2020 Aaron Bockover.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Goodbye.WordPress
{
    public sealed class ConnectionStringBuilder
    {
        static string MaskConnectionString(string connectionString)
            => Regex.Replace(
                connectionString,
                @"(password|pwd)(\s*=\s*)([^;]+)(;?)",
                "$1$2******$4",
                RegexOptions.IgnoreCase);

        internal ImmutableArray<(string Key, string Value)> Parameters { get; private set; }
            = ImmutableArray<(string, string)>.Empty;

        public ConnectionStringBuilder()
        {
        }

        internal ConnectionStringBuilder(ImmutableArray<(string Key, string Value)> parameters)
            => Parameters = parameters;

        public string? Host
        {
            get => this[nameof(Host)];
            set => this[nameof(Host)] = value;
        }

        public int? Port
        {
            get
            {
                if (this[nameof(Port)] is string portString &&
                    int.TryParse(
                        portString,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var port))
                    return port;

                return null;
            }

            set
            {
                if (value is null)
                    RemoveParameter(nameof(Port));
                else
                    AddParameter(
                        nameof(Port),
                        value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        public string? Username
        {
            get => this[nameof(Username)];
            set => this[nameof(Username)] = value;
        }

        public string? Password
        {
            get => this[nameof(Password)];
            set => this[nameof(Password)] = value;
        }

        public string? Database
        {
            get => this[nameof(Database)];
            set => this[nameof(Database)] = value;
        }


        static bool KeyEquals(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public bool TryGetParameter(string key, out string? value)
        {
            foreach (var parameter in Parameters)
            {
                if (KeyEquals(parameter.Key, key))
                {
                    value = parameter.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public string? this[string key]
        {
            get
            {
                TryGetParameter(key, out var value);
                return value;
            }

            set
            {
                if (value is null)
                    RemoveParameter(key);
                else
                    AddParameter(key, value);
            }
        }

        public void AddParameter(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            RemoveParameter(key);

            if (value is string)
                Parameters = Parameters.Add((key, value));
        }

        public void RemoveParameter(string key)
            => Parameters = Parameters.RemoveAll(p => KeyEquals(p.Key, key));

        public bool IsConfigured =>
            !string.IsNullOrEmpty(Host) &&
            !string.IsNullOrEmpty(Database);

        public string BuildConnectionString(bool maskPassword = false)
        {
            var connectionString = string.Join(';', Parameters
                .Where(item => !string.IsNullOrEmpty(item.Key))
                .Select(item => $"{item.Key}={item.Value}"));

            if (maskPassword)
                return MaskConnectionString(connectionString);

            return connectionString;
        }

        public static implicit operator string(ConnectionStringBuilder builder)
            => builder?.BuildConnectionString() ?? string.Empty;
    }
}
