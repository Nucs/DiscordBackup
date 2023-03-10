/*

Discord message backup tool
Copyright (c) 2018 never_released

This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

In jurisdictions that recognize copyright laws, the author or authors
of this software dedicate any and all copyright interest in the
software to the public domain. We make this dedication for the benefit
of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of
relinquishment in perpetuity of all present and future rights to this
software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>

*/

using System;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DiscordBackup {
    public class Message {
        [Key] public ulong Id { get; set; }
        public ulong GuildId { get; set; }
        public string time_string { get; set; }
        public string ChannelName { get; set; }
        public string UserName { get; set; }
        public string Content { get; set; }
        public bool IsTTS { get; set; }
        public bool IsPinned { get; set; }
        public ulong ChannelId { get; set; }
        public ulong UserId { get; set; }
        public long time { get; set; }
        public Message() { }

        public Message(IMessage msg, ulong guildId) {
            GuildId = guildId;
            Id = msg.Id;
            IsTTS = msg.IsTTS;
            time_string = msg.Timestamp.ToString();
            time = msg.Timestamp.ToUnixTimeSeconds();
            IsPinned = msg.IsPinned;
            Content = msg.Content;
            UserName = msg.Author.Username;
            UserId = msg.Author.Id;
            ChannelName = msg.Channel.Name;
            ChannelId = msg.Channel.Id;
        }
    }

    public class Attachment : IEquatable<Attachment> {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }
        [Key] public ulong Id { get; set; }
        public string Url { get; set; }
        public string ProxyUrl { get; set; }
        public string FileName { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public string MessageContent { get; set; }
        public byte[] Content { get; set; }
        public string LocalPath { get; set; }

        public Attachment(IAttachment attachment, ulong guildId, ulong channel, IMessage message) {
            GuildId = guildId;
            ChannelId = channel;
            MessageContent = message.Content;
            MessageId = message.Id;
            Id = attachment.Id;
            Url = attachment.Url;
            ProxyUrl = attachment.ProxyUrl;
            FileName = attachment.Filename;
            if (attachment.Width != null) {
                Width = (uint) attachment.Width.Value;
            }

            if (attachment.Height != null) {
                // width and height cannot be negative values.
                Height = (uint) attachment.Height.Value;
            }
        }

        public Attachment() { }

        public bool Equals(Attachment other) {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return GuildId == other.GuildId && ChannelId == other.ChannelId && Id == other.Id;
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Attachment) obj);
        }

        public override int GetHashCode() {
            unchecked {
                var hashCode = GuildId.GetHashCode();
                hashCode = (hashCode * 397) ^ ChannelId.GetHashCode();
                hashCode = (hashCode * 397) ^ Id.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(Attachment left, Attachment right) {
            return Equals(left, right);
        }

        public static bool operator !=(Attachment left, Attachment right) {
            return !Equals(left, right);
        }
    }

    public class MessageContext : DbContext {
        private readonly string _databasePath;
        public DbSet<Message> Messages { get; set; }
        public DbSet<Attachment> Attachments { get; set; }

        public MessageContext(string databasePath) {
            _databasePath = databasePath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        }
    }
}