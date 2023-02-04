using System;
using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using Discord.Rest;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;

namespace DiscordBackup {
    class Program {
        public async Task MainAsync(string[] args) {
            Console.WriteLine("Discord Message Backup Tool\n\n");
            DiscordSocketClient socketClient = new DiscordSocketClient();
            if (args.Length != 1) {
                Console.WriteLine("The number of arguments is incorrect.");
                Console.WriteLine("Usage: DiscordBackup.exe <token>");
                return;
            }

            //load configuration
            var options = new Options();
            new ConfigurationBuilder().AddJsonFile("appsettings.json", false, false)
                                      .Build()
                                      .Bind(options);

            //prepare connection
            TaskCompletionSource readyness = new TaskCompletionSource();
            socketClient.MessageUpdated += (cacheable, message, arg3) => { return Task.CompletedTask; };
            socketClient.Ready += () => {
                Console.WriteLine("Bot is connected!");
                readyness.TrySetResult();
                return Task.CompletedTask;
            };

            //connect
            Console.WriteLine($"Token: {new string('*', args[0].Length)}");
            string token = args[0]; //Console.ReadLine();
            await socketClient.LoginAsync(TokenType.Bot, token);
            await socketClient.StartAsync();
            if (socketClient.LoginState != LoginState.LoggedIn) {
                Console.WriteLine("Failed to log in.");
                return;
            }

            await readyness.Task;

            //prepare database
            MessageContext dbContext = new MessageContext(options.DatabasePath ?? "out/database.db");
            await dbContext.Database.EnsureCreatedAsync();

            //iterate all servers and collect backup data
            foreach (var guild in socketClient.Guilds) {
                //var ch = (SocketTextChannel) guild.Channels.First(c => c.Name == "general");
                //var t = await (ch).GetMessageAsync(1071498800285368332);

                foreach (SocketTextChannel channel in guild.Channels.Where(c => c.GetType() == typeof(SocketTextChannel))) {
                    Console.WriteLine($"Backing up channel: {guild.Name}-#{channel.Name}");
                    List<IMessage> messages = await channel.GetMessagesAsync(100).Flatten().ToListAsync();
                    ;
                    do {
                        IMessage last = null;
                        foreach (var message in messages) {
                            AddIfNotExists(dbContext.Messages, new Message(message, guild.Id), x => x.Id == message.Id);

                            if (message.Attachments.Count != 0) {
                                var attachments = message.Attachments;
                                foreach (var attachment in attachments) {
                                    var added = AddIfNotExists(dbContext.Attachments, new Attachment(attachment, guild.Id, channel.Id, message), x => x.Id == attachment.Id);
                                }
                            }

                            last = message;
                        }

                        if (last == null) //none found
                            break;
                        messages = await channel.GetMessagesAsync(last.Id, Direction.Before, 100, RequestOptions.Default).Flatten().ToListAsync();
                    } while (messages.Count > 0);
                }

                //save changes
                var changed = dbContext.ChangeTracker.Entries().Count(c => c.State != EntityState.Unchanged);
                Console.WriteLine($"Saving {changed} messages...");
                await dbContext.SaveChangesAsync();

                //handle attachments downloading
                var saveToDisk = options.SaveAttachmentsToDisk;
                var saveToDatabase = options.SaveAttachmentsToDatabase;
                if (saveToDisk || saveToDatabase) {
                    //prepare folder
                    var attachmentFolder = options.AttachmentsFolder ?? "attachments";
                    Directory.CreateDirectory(attachmentFolder);

                    //gather undownloaded attachments
                    List<(bool Disk, bool Database, Attachment Attachment)> pendingDownloadAttachments = new();
                    var files = Directory.GetFiles(attachmentFolder, "*.jpg").Select(Path.GetFileNameWithoutExtension).ToList();
                    foreach (var attachment in dbContext.Attachments.AsNoTracking().ToList()) {
                        bool toDb = false;
                        bool toDisk = false;
                        if (saveToDatabase && attachment.Content == null) {
                            toDb = true;
                        }

                        if (saveToDisk && !files.Contains(attachment.Id.ToString())) {
                            toDisk = true;
                        }

                        if (toDb || toDisk)
                            pendingDownloadAttachments.Add((toDisk, toDb, attachment));
                    }

                    //download attachments
                    Console.WriteLine($"Saving {pendingDownloadAttachments.Count} attachments...");
                    using var client = new System.Net.Http.HttpClient();
                    foreach ((bool toDisk, bool toDb, Attachment attachment) in pendingDownloadAttachments) {
                        var response = await client.GetAsync(attachment.Url);
                        var bytes = await response.Content.ReadAsByteArrayAsync();

                        if (toDisk) {
                            await File.WriteAllBytesAsync(Path.Combine(attachmentFolder, $"{attachment.Id}.jpg"), bytes);
                            await File.WriteAllTextAsync(Path.Combine(attachmentFolder, $"{attachment.Id}.txt"), attachment.MessageContent);
                        }

                        if (toDb) {
                            if (dbContext.Entry(attachment).State == EntityState.Detached)
                                dbContext.Attach(attachment);
                            attachment.Content = bytes;
                            dbContext.Attachments.Update(attachment);
                        }
                    }

                    await dbContext.SaveChangesAsync();
                }
            }

            Console.WriteLine("Message backup finished.");

            if (options.UseGit) {
                try {
                    Console.WriteLine("Backuping to git");
                    options.RootOut = Path.GetFullPath(options.RootOut);
                    Directory.CreateDirectory(options.RootOut);

                    Credentials credentials;
                    if (options.GitCredentials != null) {
                        credentials = new UsernamePasswordCredentials() { Username = options.GitCredentials.Username, Password = options.GitCredentials.Password };
                    } else {
                        credentials = new DefaultCredentials();
                    }

                    Repository repo;
                    try {
                        repo = new LibGit2Sharp.Repository(Path.GetFullPath(options.RootOut));
                    } catch (RepositoryNotFoundException) {
                        Directory.CreateDirectory("./tmp");
                        try {
                            Repository.Clone(options.GitRepository, Path.GetFullPath("./tmp"), new CloneOptions() {
                                FetchOptions = new FetchOptions() { CredentialsProvider = delegate { return credentials; } },
                                CredentialsProvider = new CredentialsHandler((url, url2, types) =>
                                                                                 credentials),
                                CertificateCheck = delegate { return true; },
                            });
                            Directory.Move("./tmp/.git", Path.Combine(options.RootOut, ".git"));
                            repo = new LibGit2Sharp.Repository(Path.GetFullPath(options.RootOut));
                        } finally {
                            Directory.Delete("./tmp", true);
                        }
                    }

                    var status = repo.RetrieveStatus();
                    if (status.IsDirty) {
                        Console.WriteLine($"{status.Count()} Changes found, committing...");

                        //stage
                        foreach (var file in status) {
                            if (file.State is FileStatus.Ignored or FileStatus.Conflicted or FileStatus.Unaltered)
                                continue;
                            repo.Index.Add(file.FilePath);
                        }

                        repo.Index.Write();

                        var author = new Signature("DiscordBackup", "discordbackup@localhost", DateTimeOffset.Now);
                        repo.Commit($"Backup {DateTime.UtcNow.ToString("s")}", author, author, new CommitOptions() { AllowEmptyCommit = true });
                        Console.WriteLine("Changes committed.");
                    } else {
                        Console.WriteLine("No changes found.");
                    }

                    repo.Network.Push(repo.Head, new PushOptions() {
                        CredentialsProvider = delegate { return credentials; },
                    });
                } catch (Exception e) {
                    Console.WriteLine($"Unable to push to git\n{e}");
                }
            }
        }

        static void Main(string[] args) =>
            new Program().MainAsync(args).GetAwaiter().GetResult();

        public virtual EntityEntry<T>? AddIfNotExists<T>(DbSet<T> dbSet, T entity, Expression<Func<T, bool>> predicate = null) where T : class {
            var exists = predicate != null ? dbSet.Any(predicate) : dbSet.Any();
            return !exists ? dbSet.Add(entity) : null;
        }
    }
}