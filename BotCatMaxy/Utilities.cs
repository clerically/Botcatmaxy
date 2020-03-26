﻿using System.Diagnostics.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections;
using Discord.WebSocket;
using Discord.Commands;
using BotCatMaxy.Data;
using MongoDB.Driver;
using Discord.Rest;
using MongoDB.Bson;
using System.Linq;
using System.Text;
using Humanizer;
using Serilog;
using Discord;
using System;
using Discord.Net;

namespace BotCatMaxy {
    public static class Utilities {
        public static string BasePath = "/home/bob_the_daniel/Data/";
        public static ILogger logger;

        public static IMongoCollection<BsonDocument> GetCollection(this IGuild guild, bool createDir = true) {
            if (guild == null)
                throw new ArgumentNullException(nameof(guild));
            var db = MainClass.dbClient.GetDatabase("Settings");
            var guildCollection = db.GetCollection<BsonDocument>(guild.Id.ToString());
            var ownerCollection = db.GetCollection<BsonDocument>(guild.OwnerId.ToString());
            if (guildCollection.CountDocuments(new BsonDocument()) > 0) {
                return guildCollection;
            } else if (ownerCollection.CountDocuments(new BsonDocument()) > 0 || createDir) {
                return ownerCollection;
            }

            return null;
        }

        public static bool HasAdmin(this SocketGuildUser user) {
            if (user?.Guild?.Owner == user) {
                return true;
            }

            bool hasAdmin = false;
            foreach (SocketRole role in (user).Roles) {
                if (role.Permissions.Administrator) {
                    hasAdmin = true;
                }
            }

            return hasAdmin;
        }

        public static bool CanWarn(this SocketGuildUser user) {
            if (HasAdmin(user)) {
                return true;
            }

            foreach (SocketRole role in user.Roles) {
                if (role.Permissions.BanMembers) {
                    return true;
                }
            }
            ModerationSettings settings = user.Guild.LoadFromFile<ModerationSettings>();
            if (settings != null && settings.ableToWarn != null && settings.ableToWarn.Count > 0) {
                List<SocketRole> rolesAbleToWarn = new List<SocketRole>();
                foreach (ulong roleID in settings.ableToWarn) {
                    rolesAbleToWarn.Add(user.Guild.GetRole(roleID));
                }
                if (user.Roles.Intersect(rolesAbleToWarn).Any()) {
                    return true;
                }
            }
            return false;
        }

        public static List<ulong> RoleIDs(this SocketGuildUser user) {
            List<ulong> IDs = new List<ulong>();
            foreach (SocketRole role in user.Roles) {
                IDs.Add(role.Id);
            }
            return IDs;
        }

        public static bool CantBeWarned(this SocketGuildUser user) {
            if (HasAdmin(user)) return true;

            ModerationSettings settings = user.Guild.LoadFromFile<ModerationSettings>();
            if (settings != null) {
                List<SocketRole> rolesUnableToBeWarned = new List<SocketRole>();
                foreach (ulong roleID in settings.cantBeWarned) rolesUnableToBeWarned.Add(user.Guild.GetRole(roleID));
                if (user.Roles.Intersect(rolesUnableToBeWarned).Any()) return true;
            }
            return false;
        }

        public static SocketGuild GetGuild(SocketGuildChannel channel) {
            return channel.Guild;
        }

        public static void RemoveNullEntries<T>(this ICollection<T> list) {
            if (list != null || list.Count > 0) {
                foreach (T thing in list) {
                    if (thing == null) {
                        list.Remove(thing);
                    }
                }
            }
        }

        public static async Task Log(this LogMessage message) {
            string finalMessage = message.Source.PadRight(8) + message.Message;
            if (message.Severity <= LogSeverity.Error) { //If severity is Critical or Error
                Console.Beep();
                var errorEmbed = new EmbedBuilder()
                    .WithAuthor(BotInfo.user)
                    .WithTitle(message.Source)
                    .AddField(message.Severity.ToString(), message.Message.ToString())
                    .WithCurrentTimestamp();
                await BotInfo.logChannel.SendMessageAsync(embed: errorEmbed.Build());
            }
            switch (message.Severity) {
                case LogSeverity.Critical:
                    if (message.Exception != null) logger.Fatal(message.Exception, finalMessage);
                    else logger.Fatal(finalMessage);
                    break;
                case LogSeverity.Error:
                    if (message.Exception != null) logger.Error(message.Exception, finalMessage);
                    else logger.Error(finalMessage);
                    break;
                case LogSeverity.Warning:
                    if (message.Exception != null) logger.Warning(message.Exception, finalMessage);
                    else logger.Warning(finalMessage);
                    break;
                case LogSeverity.Info:
                    logger.Information(finalMessage);
                    break;
                case LogSeverity.Verbose:
                    logger.Verbose(finalMessage);
                    break;
                case LogSeverity.Debug:
                    logger.Debug(finalMessage);
                    break;
            }
            //Console.Write("> ");
        }

        public static string Suffix(this int num) {
            if (num.ToString().EndsWith("11")) return num.ToString() + "th";
            if (num.ToString().EndsWith("12")) return num.ToString() + "th";
            if (num.ToString().EndsWith("13")) return num.ToString() + "th";
            if (num.ToString().EndsWith("1")) return num.ToString() + "st";
            if (num.ToString().EndsWith("2")) return num.ToString() + "nd";
            if (num.ToString().EndsWith("3")) return num.ToString() + "rd";
            return num.ToString() + "th";
        }

        public static async Task AssertAsync(this bool assertion, string message = "Assertion failed") {
            if (assertion == false) {
                await Log(new LogMessage(LogSeverity.Error, "Assert", message));
            }
        }

        public static async Task AssertWarnAsync(this bool assertion, string message = "Assertion failed") {
            if (assertion == false) {
                await Log(new LogMessage(LogSeverity.Warning, "Assert", message));
            }
        }

        public static bool IsNullOrEmpty(this string s) {
            if (s == null || s == "")
                return true;
            return false;
        }

        public static string StrippedOfPing(this string s) {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) {
                if (c == '@') {
                    if (s.ToArray()[sb.Length] != ' ') sb.Append('a');
                } else sb.Append(c);
            }

            return sb.ToString();
        }

        public static string NickOrUsername(this SocketGuildUser user) {
            if (user == null) {
                new LogMessage(LogSeverity.Error, "Utility", "User is null").Log();
                return "``NULL USER``";
            }
            if (user.Nickname.IsNullOrEmpty()) return user.Username;
            else return user.Nickname;
        }

        public static TimeSpan? ToTime(this string s) {
            try {
                double amount = double.Parse(s.Remove(s.Length - 1));
                if (s.ToLower().EndsWith("w")) {
                    return TimeSpan.FromDays(amount * 7);
                } else if (s.ToLower().EndsWith('d')) {
                    return TimeSpan.FromDays(amount);
                } else if (s.ToLower().EndsWith('h')) {
                    return TimeSpan.FromHours(amount);
                } else if (s.ToLower().EndsWith("mi")) {
                    return TimeSpan.FromMinutes(amount);
                } else if (s.ToLower().EndsWith("mo")) {
                    return TimeSpan.FromDays(amount * 30.4368);
                } else if (s.ToLower().EndsWith("y")) {
                    return TimeSpan.FromDays(amount * 365.2425);
                }
            } catch { }
            return null;
        }

        public static bool IsNullOrEmpty(this ICollection list) {
            if (list == null || list.Count == 0) return true;
            else return false;
        }
        public static bool NotEmpty<T>(this IEnumerable<T> list, int needAmount = 0) {
            if (list == null || list.ToArray().Length <= needAmount) return false;
            else return true;
        }

        public static string Pluralize(this string s, float num) {
            if (num == 1) return s;
            else return s.Pluralize();
        }

        public static void ClearLine() {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }

        public static string JoinAll(this IEnumerable<string> list) {
            string s = "";
            foreach (string element in list) {
                if (s.Length > 0) s += " ";
                s += element;
            }
            return s;
        }

        public static async Task TryNotify(this Task<RestUser> task, string message) {
            (await task)?.TryNotify(message);
        }

        public static bool TryNotify(this IUser user, string message) {
            try {
                var sentMessage = user.GetOrCreateDMChannelAsync()?.Result.SendMessageAsync(message);
                if (sentMessage == null) return false;
                return true;
            } catch {
                return false;
            }
        }
        public static bool TryNotify(this IUser user, Embed embed) {
            try {
                var sentMessage = user.GetOrCreateDMChannelAsync()?.Result.SendMessageAsync(embed: embed);
                if (sentMessage == null) return false;
                return true;
            } catch {
                return false;
            }
        }

        public static void DeleteOrRespond(this SocketMessage message, string toSay, IGuild guild, LogSettings settings = null) {
            if (settings == null) settings = guild.LoadFromFile<LogSettings>(false);
            if (guild.GetChannelAsync(settings?.pubLogChannel ?? 0).Result == null) message.Channel.SendMessageAsync(toSay);
            else {
                Logging.AddToDeletedCache(message.Id);
                _ = message.DeleteAsync();
                guild.GetTextChannelAsync(settings.pubLogChannel ?? 0).Result.SendMessageAsync($"{message.Author.Mention}, {toSay}");
            }
        }

        public static string ListItems(this ICollection<string> list, string joiner = " ") {
            string items = null;
            if (list.NotEmpty()) {
                list.RemoveNullEntries();
                foreach (string item in list) {
                    if (items == null) items = "";
                    else items += joiner;
                    items += item;
                }
            }
            return items;
        }

        public static bool CanActOn(this SocketGuildUser focus, SocketGuildUser comparer) {
            if (focus.Roles.Select(role => role.Position).Max() > comparer.Roles.Select(role => role.Position).Max())
                return true;
            return false;
        }

        public static string LimitedHumanize(this TimeSpan timeSpan, int precision = 2) {
            return timeSpan.Humanize(precision, maxUnit: Humanizer.Localisation.TimeUnit.Day, minUnit: Humanizer.Localisation.TimeUnit.Second);
        }

        public static TimeSpan GetTimeAgo(this IMessage message) {
            Contract.Requires(message != null);
            return DateTime.Now - message.Timestamp;
        }

        public static bool TryGetChannel(this IGuild guild, ulong id, out IGuildChannel channel) {
            Contract.Requires(guild != null);
            channel = guild.GetChannelAsync(id).GetAwaiter().GetResult();
            return channel != null;
        }

        public static string Name(this UserRef userRef, bool showIDWithUser = false, bool showRealName = false) {
            if (userRef == null) return "``ERROR``";
            string name = null;
            if (userRef.gUser?.Nickname != null) {
                name = userRef.gUser.Nickname.StrippedOfPing();
                if (showRealName) //done since people with nicknames might have an innapropriate name under the nickname
                    name += $" aka {userRef.gUser.Username.StrippedOfPing()}";
            }
            if (name == null && userRef.user != null) name = userRef.user.Username.StrippedOfPing();
            if (name != null) {
                if (showIDWithUser) name += $" ({userRef.ID})";
                return name;
            }
            return $"User with ID:{userRef.ID}";
        }

        public static string Mention(this UserRef userRef) {
            if (userRef == null) return "``ERROR``";
            if (userRef.user != null) return userRef.user.Mention;
            return $"User with ID:{userRef.ID}";
        }

        public static EmbedBuilder WithAuthor(this EmbedBuilder embed, UserRef userRef) {
            Contract.Requires(embed != null);
            if (userRef.user != null) embed.WithAuthor(userRef.user);
            else embed.WithAuthor($"Unkown user with ID:{userRef.ID}");
            return embed;
        }

        public static void RecordAct(this ulong userID, IGuild guild, TempAct tempAct, string type, string loglink = null) {
            var acts = userID.LoadActRecord(guild, true);
            acts.Add(new ActRecord() {
                type = type,
                length = tempAct.length,
                logLink = loglink,
                reason = tempAct.reason,
                time = tempAct.dateBanned
            });
            userID.SaveActRecord(guild, acts);
        }

        public static async Task<IUser> SuperGetUser(this DiscordSocketClient client, ulong ID) {
            for (int i = 0; i < 3; i++) {
                try {
                    IUser user = client.GetUser(ID);
                    user ??= await client.Rest.GetUserAsync(ID, new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                    return user;
                } catch (HttpException e) {
                    if (i == 2 || e.HttpCode != System.Net.HttpStatusCode.ServiceUnavailable) throw;
                }
            }
            throw new Exception("SuperGetUser ran out of tries without throwing proper exception?");
        }


        public static async Task<IGuildUser> SuperGetUser(this DiscordSocketClient client, SocketGuild guild, ulong ID) {
            var requestOptions = new RequestOptions() { RetryMode = RetryMode.AlwaysRetry };
            for (int i = 0; i < 3; i++) {
                try {
                    IGuildUser user = guild.GetUser(ID);
                    if (user == null) {
                        RestGuild restGuild = await client.Rest.GetGuildAsync(guild.Id, requestOptions);
                        user = await restGuild.GetUserAsync(ID, requestOptions);
                    }
                    return user;
                } catch (HttpException e) {
                    if (i == 2 || e.HttpCode != System.Net.HttpStatusCode.ServiceUnavailable) throw;
                }
            }
            throw new Exception("SuperGetUser ran out of tries without throwing proper exception?");
        }
    }
}
