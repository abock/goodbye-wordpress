namespace WPExportApp {
  using System.Threading.Tasks;
  using Goodbye.WordPress;

  class WPExportMain {
    /// <summary>
    /// Entry Point
    /// </summary>
    /// <param name="args">CLA</param>
    static async Task Main(string[] args)
    {
      var config = new JsonConfig();
      await config.Load();
      if (config.Options == null)
        throw new System.NullReferenceException();

      var exporter = WordPressExporter.Create(
      postReader: new MysqlPostReader(
        new ConnectionStringBuilder {
          Host = config.Options.Host,
          Database = config.Options.Database,
          Username = config.Options.Username,
          Password = config.Options.Password
          // , TlsVersion = config.Options.TlsVersion
        }),
        contentOutputDirectory: config.Options.ContentOutputDirectory,
        archiveOutputFilePath: config.Options.ArchiveOutputFilePath,
        // And now the delegate...
        @delegate: new CustomExporterDelegate(config.Options.Patterns)
      );

      await exporter.ExportAsync();
    }
  }


  sealed class CustomExporterDelegate : WordPressExporterDelegate
  {
    JsonConfig.ReplacePattern[] StrPatterns;

    public CustomExporterDelegate(JsonConfig.ReplacePattern[] patterns)
    {
        StrPatterns = patterns;
    }

    // Replace weird unicode chars
    private string SubstituteCommon(string str) =>
      str.Replace("‘", "'").Replace("’", "'")
        .Replace("“", "\"").Replace("”", "\"");

    private string ProcessContent(string content) {
      content = SubstituteCommon(content)
        .Replace("http://", "https://");

      if (StrPatterns != null && StrPatterns.Length > 0)
        foreach( var pattern in StrPatterns)
          content = content.Replace(pattern.Needle, pattern.Substitute);
      
      return content;
    }

    /// <summary>Process post contents</summary>
    public override Post ProcessPost(
      WordPressExporter exporter,
      Post post)
    // Perform the default post processing first by calling base
    => base.ProcessPost(exporter, post) with
    {
      Content = ProcessContent(post.Content)
    };

    /// <summary>Add 'CustomMetadata' to each post's YAML front matter</summary>
    public override void PopulatePostYamlFrontMatter(
      WordPressExporter exporter,
      Post post,
      SharpYaml.Serialization.YamlMappingNode rootNode)
    {
      base.PopulatePostYamlFrontMatter(exporter, post, rootNode);
      // rootNode.Add("CustomMetadata", "Some Value");
    }
  }
}