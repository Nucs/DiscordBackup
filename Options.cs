namespace DiscordBackup; 

public class Options {
    public bool SaveAttachmentsToDatabase { get; set; }
    public bool SaveAttachmentsToDisk { get; set; }
    public string AttachmentsFolder { get; set; }
    public string DatabasePath { get; set; }
    public bool UseGit { get; set; }
    public bool SaveAttachmentsFolderToGit { get; set; }
    public bool SaveDatabaseToGit { get; set; }
    public string RootOut { get; set; }
    public string GitRepository { get; set; }
    public GitCredentials GitCredentials { get; set; }
}

public class GitCredentials {
    public string Username { get; set; }
    public string Password { get; set; }
}