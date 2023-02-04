Discord message backup tool
===========================
This tool allows you to backup multiple Discord servers chat content to a local SQLite3 database as well as saving 
the attachments to a local directory.

Supports uploading the database to a git repository as a cloud-backup (use github or gitlab)

See appsettings.json for configuration options.

Instructions:
------------
Usage:
```sh
DiscordBackup.exe <bot-token>
```

Runs on .NET 7.0. You can download it from https://www.microsoft.com/net/download/core

Create a bot and get it's token at https://discordapp.com/developers/applications/me <br/>
Add the bot to your server<br/>
Make sure to give it permissions to read message's content.

