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
