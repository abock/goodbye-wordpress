## Example Json Config
Default config path: "$Env:LocalAppData\WPExportConfig.json"

For example, `C:\Users\UserName\AppData\Local\WPExportConfig.json`

An example config looks like following,

    {
      "Host": "localhost",
      "Username": "user",
      "Password": "your_password",
      "Database": "wordpress_db_name",
      "ContentOutputDirectory": "posts",
      "ArchiveOutputFilePath": "posts/archive.json",
      "Patterns": [
        {
          "Needle": "old-domain.com",
          "Substitute": "new-domain.com"
        }
      ]
    }


For example without json config, please have a look at [primary ReadMe](https://github.com/abock/goodbye-wordpress#example-programcs).