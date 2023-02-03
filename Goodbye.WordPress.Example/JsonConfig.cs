namespace WPExportApp {
  using System;
  using System.Threading.Tasks;

  /// <summary>
  /// Credentials Related
  /// Provides
  /// - Credentials
  /// - Additional options i.e., for SSL
  /// - Patterns for substitution/pre-processing content
  /// </summary>
  class JsonConfig {
    public WPExpJsonConfig? Options { get; set; }

    /// <summary>
    /// Config file path
    /// </summary>
    private string JsonConfigFilePath { get; set; }

    /// <remarks>
    /// <see href="https://docs.microsoft.com/en-us/dotnet/api/system.environment.specialfolder">
    /// Accessing Local App Data via Environment
    /// </see>
    /// </remarks>
    public JsonConfig() {
      JsonConfigFilePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        + @"\WPExportConfig.json";

      if (!System.IO.File.Exists(JsonConfigFilePath)) {
        throw new InvalidOperationException($"Required config: {JsonConfigFilePath} not found!" + 
          "Please create the config file and run this application again.");
      }
    }

    public class ReplacePattern {
      public string Needle { get; set; }
      public string Substitute { get; set; }
    }

    /// <summary>
    /// Structure to read records from config file (json format)
    /// Self explanatory props
    /// </summary>
    public class WPExpJsonConfig {
      public string Host { get; set; }
      public string Database { get; set; }
      public string Username { get; set; }
      public string Password { get; set; }
      public string TlsVersion { get; set; }
      public string ContentOutputDirectory { get; set; }
      public string ArchiveOutputFilePath { get; set; }
      /// <summary>
      /// Some of the patterns might be internal only and should not be published online
      /// Therefore, it might be good idea to keep them in the json config file.
      /// </summary>
      public ReplacePattern[] Patterns { get; set; }
    }


    /// <summary>
    /// Read and Parse Config file
    /// </summary>
    public async Task Load() {
      using System.IO.FileStream openStream = System.IO.File.OpenRead(JsonConfigFilePath);
      Options = await System.Text.Json.JsonSerializer.DeserializeAsync<WPExpJsonConfig>(openStream);

      if (Options == null || string.IsNullOrEmpty(Options.Host) || string.IsNullOrEmpty(
        Options.Database) || string.IsNullOrEmpty(Options.Username) || string.IsNullOrEmpty(
          Options.Password)) {
            throw new NullReferenceException("Database Credentials are empty in Json config file!");
      }
    }
  }
}
