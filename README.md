# Goodbye WordPress

[![.NET](https://github.com/abock/goodbye-wordpress/actions/workflows/dotnet.yml/badge.svg)](https://github.com/abock/goodbye-wordpress/actions/workflows/dotnet.yml)
[![NuGet Badge](https://buildstats.info/nuget/goodbye-wordpress)](https://www.nuget.org/packages/goodbye-wordpress/)
[![NuGet Badge](https://buildstats.info/nuget/Goodbye.WordPress)](https://www.nuget.org/packages/Goodbye.WordPress/)
[![License](https://img.shields.io/badge/license-MIT%20License-blue.svg)](LICENSE)

Goodbye WordPress is a customizable library and command line tool for exporting posts from a WordPress MySQL database to static Markdown or HTML files, with YAML front-matter for metadata preservation. It is intended as a starting point for migrating away from a WordPress blog to a static site.

It further supports saving images referenced in the original posts and can generate a single-file archive of all exported posts in JSON format, in case the export process needs to run again after the MySQL database has been taken down.

## Supported WordPress Versions

This tool was developed to export a very legacy WordPress blog to a static site. As such, only a single database revision has been vetted. It is likely that older and newer versions are supported.

| Release      |              | Through      |              | Database Version |
| ------------ | ------------ | ------------ | ------------ | ---------------- |
| **4.7**      | _2016-12-06_ | **4.9.15**   | _2020-06-10_ | `38590`          |

Pass `--ignore-unsupported-db-versions` to the command line tool to ignore the version check.

_Please_ open a pull request to add other vetted versions [MysqlPostReader](Goodbye.WordPress/MysqlPostReader.cs#L20).

## Command Line Tool

### Install

```
dotnet tool install --global goodbye-wordpress
```

### Options

```
usage: goodbye-wordpress [OPTIONS] [JSON_INPUT_FILE]

Options:

  -?, --help                 Show this help
  -v, --verbose              Use verbose logging
  -q, --quiet                Use quiet logging (errors only); synonym for -v-

Post Options:

  -o, --output-dir=VALUE     Set the output directory for posts and images
      --format=FORMAT        Output FORMAT: markdown | html | raw
      --serialize-json=FILE  Serialize the entire post set to FILE

MySQL Options:

  -h, --host=VALUE           Connect to host
  -P, --port=VALUE           Connect through port
  -u, --user=VALUE           User for login
  -p, --password=VALUE       Password to use
  -D, --database=VALUE       Database to use
  -i, --ignore-unsupported-db-versions
                             Ignore unsupported WordPress database versions
```

### Export and Archive Example

```bash
goodbye-wordpress \
  -h localhost \
  -u user \
  -p *** \
  -D wordpressdb \
  -o exported-posts \
  --serialize-json exported-posts/archive.json
```

See the contents of the `exported-posts/` directory.

## Customization with the Library

```
dotnet new console
dotnet add package Goodbye.WordPress
```

### Immutability

Note that with the exception of `ConnectionStringBuilder` and `WordPressExporterDelegate`, all types are _immutable_. For example, to transform a `Post` object's content, try `post = post with { Content = post.Content.Replace("A", "B") }`.

### Example Program.cs

Creating the exporter via API here is equivalent to the command line invocation above, with the exception of providing a custom exporter delegate that will perform additional processing steps.

```csharp
using Goodbye.WordPress;

var exporter = WordPressExporter.Create(
  postReader: new MysqlPostReader(new ConnectionStringBuilder
  {
    Host = "localhost",
    Username = "user",
    Password = "***",
    Database = "wordpressdb"
  }),
  contentOutputDirectory: "exported-posts",
  archiveOutputFilePath: "exported-posts/archive.json",

  // And now the delegate...
  @delegate: new CustomExporterDelegate());

await exporter.ExportAsync();

sealed class CustomExporterDelegate : WordPressExporterDelegate
{
  /// <summary>Process post contents</summary>
  public override Post ProcessPost(
    WordPressExporter exporter,
    Post post)
    // Perform the default post processing first by calling base
    => base.ProcessPost(exporter, post) with
    {
      // Then replace '--' with Unicode em dash '—'
      Content = post.Content.Replace("--", "—")
    };

  /// <summary>Add 'CustomMetadata' to each post's YAML front matter</summary>
  public override void PopulatePostYamlFrontMatter(
    WordPressExporter exporter,
    Post post,
    SharpYaml.Serialization.YamlMappingNode rootNode)
  {
    base.PopulatePostYamlFrontMatter(exporter, post, rootNode);
    rootNode.Add("CustomMetadata", "Some Value");
  }
}
```