﻿using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.WebSocket;
using BotCatMaxy.Data;
using Discord.Commands;
using Discord.Rest;
using System.Linq;
using System.Text;
using BotCatMaxy;
using Humanizer;
using Discord;
using System;
using BotCatMaxy.Models;

namespace BotCatMaxy
{
    public class Logging
    {
        public static FixedSizedQueue<ulong> deletedMessagesCache = new FixedSizedQueue<ulong>(10);
        private readonly DiscordSocketClient _client;
        public Logging(DiscordSocketClient client)
        {
            _client = client;

            _ = SetUpAsync();
        }

        public async Task SetUpAsync()
        {
            _client.MessageDeleted += HandleDelete;
            _client.MessageUpdated += LogEdit;
            _client.MessageReceived += HandleNew;

            await new LogMessage(LogSeverity.Info, "Logs", "Logging set up").Log();
        }

        public async Task HandleNew(IMessage message) => await Task.Run(() => LogNew(message)).ConfigureAwait(false);
        public async Task LogNew(IMessage message)
        {
            if (message.Channel as SocketGuildChannel != null && message.MentionedRoleIds != null && message.MentionedRoleIds.Count > 0)
            {
                SocketGuild guild = (message.Channel as SocketGuildChannel).Guild;
                LogMessage("Role ping", message, guild, true);
            }
        }

        async Task LogEdit(Cacheable<IMessage, ulong> cachedMessage, SocketMessage newMessage, ISocketMessageChannel channel)
        {
            try
            {
                //Just makes sure that it's not logged when it shouldn't be
                if (!(channel is SocketGuildChannel)) return;
                SocketGuild guild = (channel as SocketGuildChannel).Guild;
                IMessage oldMessage = await cachedMessage.GetOrDownloadAsync();
                if (oldMessage?.Content == newMessage.Content || newMessage.Author.IsBot || guild == null) return;
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                //if settings or the log channel are null, or edit logging is disabled, just stop
                if (settings?.logChannel == null || !settings.logEdits) return;
                SocketTextChannel logChannel = guild.GetChannel(settings.logChannel.Value) as SocketTextChannel;
                if (logChannel == null) return;

                var embed = new EmbedBuilder();
                if (string.IsNullOrEmpty(oldMessage?.Content))
                {
                    embed.AddField($"Message was edited in #{newMessage.Channel.Name} from",
                    "`This message had no text or was null`");
                }
                else
                {
                    embed.AddField($"Message was edited in #{newMessage.Channel.Name} from",
                    oldMessage.Content.Truncate(1020));
                }
                if (string.IsNullOrEmpty(newMessage.Content))
                {
                    embed.AddField($"Message was edited in #{newMessage.Channel.Name} to",
                    "`This message had no text or is null`");
                }
                else
                {
                    embed.AddField($"Message was edited in #{newMessage.Channel.Name} to",
                    newMessage.Content.Truncate(1020));
                }

                embed.AddField("Message Link", "[Click Here](" + newMessage.GetJumpUrl() + ")", false);
                embed.WithFooter("ID: " + newMessage.Id)
                    .WithAuthor(newMessage.Author)
                    .WithColor(Color.Teal)
                    .WithCurrentTimestamp();

                await logChannel.SendMessageAsync(embed: embed.Build());
            }
            catch (Exception exception)
            {
                await new LogMessage(LogSeverity.Error, "Logging", exception.Message, exception).Log();
            }
        }

        public static string LogMessage(string reason, IMessage message, SocketGuild guild = null, bool addJumpLink = false, Color? color = null)
        {
            try
            {
                if (message == null) return null;
                if (deletedMessagesCache.Any(delMsg => delMsg == message.Id)) return null;

                if (guild == null)
                {
                    guild = Utilities.GetGuild(message.Channel as SocketGuildChannel);
                    if (guild == null)
                    {
                        return null;
                    }
                }

                LogSettings settings = guild?.LoadFromFile<LogSettings>();
                SocketGuildChannel gChannel = guild?.GetChannel(settings?.logChannel ?? 0);
                if (settings == null || gChannel == null || !settings.logDeletes)
                {
                    return null;
                }
                SocketTextChannel logChannel = gChannel as SocketTextChannel;

                var embed = new EmbedBuilder();
                SocketTextChannel channel = message.Channel as SocketTextChannel;
                if (message.Content == null || message.Content == "")
                {
                    embed.AddField(reason + " in #" + message.Channel.Name,
                    "`This message had no text`", true);
                }
                else
                {
                    embed.AddField(reason + " in #" + message.Channel.Name,
                    message.Content.Truncate(1020), true);
                }

                if (addJumpLink)
                {
                    embed.AddField("Message Link", "[Click Here](" + message.GetJumpUrl() + ")", true);
                }

                string links = "";
                if (message.Attachments.NotEmpty())
                {
                    foreach (IAttachment attachment in message.Attachments)
                    {
                        links += " " + attachment.ProxyUrl;
                    }
                }

                embed.WithFooter($"Message ID:{message.Id} • User ID:{message.Author.Id}")
                    .WithAuthor(message.Author)
                    .WithColor(color ?? Color.Blue)
                    .WithCurrentTimestamp();
                string link = logChannel.SendMessageAsync(embed: embed.Build()).Result.GetJumpUrl();
                if (!links.IsNullOrEmpty()) logChannel.SendMessageAsync("The message above had these attachments:" + links);
                return link;
            }
            catch (Exception exception)
            {
                _ = new LogMessage(LogSeverity.Error, "Logging", exception.Message, exception).Log();
            }
            return null;
        }

        public static async Task<IUserMessage> LogWarn(IGuild guild, IUser warner, ulong warneeID, string reason, string warnLink)
        {
            try
            {
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                ITextChannel channel = guild.GetTextChannelAsync(settings?.pubLogChannel ?? settings?.logChannel ?? 0).Result;
                if (channel == null) return null;

                var embed = new EmbedBuilder();
                embed.WithAuthor(warner);
                IGuildUser gWarnee = guild.GetUserAsync(warneeID).Result;
                if (gWarnee != null)
                {
                    if (gWarnee.Nickname.IsNullOrEmpty())
                        embed.AddField($"{gWarnee.Username} ({gWarnee.Id}) has been warned", "For " + reason);
                    else
                        embed.AddField($"{gWarnee.Nickname} aka {gWarnee.Username} ({warneeID}) has been warned", "For " + reason);
                }
                else
                    embed.AddField($"({warneeID}) has been warned", "For " + reason);

                if (!warnLink.IsNullOrEmpty()) embed.AddField("Jumplink", $"[Click Here]({warnLink})");
                embed.WithColor(Color.Gold);
                embed.WithCurrentTimestamp();

                return await channel.SendMessageAsync(embed: embed.Build());
            }
            catch (Exception e)
            {
                await new LogMessage(LogSeverity.Error, "Logging", "Error", e).Log();
            }
            return null;
        }

        public static void LogTempAct(IGuild guild, IUser warner, UserRef subject, string actType, string reason, string warnLink, TimeSpan length)
        {
            try
            {
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                ITextChannel channel = guild.GetTextChannelAsync(settings?.pubLogChannel ?? settings?.logChannel ?? 0).Result;
                if (channel == null) return;

                var embed = new EmbedBuilder();
                embed.WithAuthor(warner);
                if (length == TimeSpan.Zero) //if not for forever
                    embed.AddField($"{subject.Name(true, true)} has been perm {actType}ed", $"Because of {reason}");
                else
                    embed.AddField($"{subject.Name(true, true)} has been temp-{actType}ed for {length.LimitedHumanize()}", $"Because of {reason}");
                if (!warnLink.IsNullOrEmpty()) embed.AddField("Jumplink", $"[Click Here]({warnLink})");
                embed.WithColor(Color.Red);
                embed.WithCurrentTimestamp();

                channel.SendMessageAsync(embed: embed.Build());
                return;
            }
            catch (Exception e)
            {
                _ = new LogMessage(LogSeverity.Error, "Logging", "Error", e).Log();
            }
            return;
        }

        public static void LogEndTempAct(IGuild guild, IUser warnee, string actType, string reason, TimeSpan length)
        {
            try
            {
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                ITextChannel channel = guild.GetTextChannelAsync(settings?.pubLogChannel ?? settings?.logChannel ?? 0).Result;
                if (channel == null)
                    return;

                var embed = new EmbedBuilder();
                embed.WithAuthor(warnee);
                if (!(warnee is SocketGuildUser) || (warnee as SocketGuildUser).Nickname.IsNullOrEmpty())
                    embed.AddField($"{warnee.Username} ({warnee.Id}) has been un{actType}ed", $"After {length.LimitedHumanize(2)}, because of {reason}");
                else
                    embed.AddField($"{(warnee as SocketGuildUser).Nickname} aka {warnee.Username} ({warnee.Id}) has been un{actType}ed", $"After {length.LimitedHumanize(2)}, because of {reason}");
                //if (!warnLink.IsNullOrEmpty()) embed.AddField("Jumplink", warnLink);
                embed.WithColor(Color.Green);
                embed.WithCurrentTimestamp();

                channel.SendMessageAsync(embed: embed.Build()).Result.GetJumpUrl();
                return;
            }
            catch (Exception e)
            {
                _ = new LogMessage(LogSeverity.Error, "Logging", "Error", e).Log();
            }
            return;
        }

        public static void LogManualEndTempAct(IGuild guild, IUser warnee, string actType, DateTime dateHappened)
        {
            try
            {
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                ITextChannel channel = guild.GetTextChannelAsync(settings?.pubLogChannel ?? settings?.logChannel ?? 0).Result;
                if (channel == null)
                    return;

                var embed = new EmbedBuilder();
                embed.WithAuthor(warnee);
                if (!(warnee is SocketGuildUser) || (warnee as SocketGuildUser).Nickname.IsNullOrEmpty())
                    embed.AddField($"{warnee.Username} ({warnee.Id}) has been manually un{actType}ed", $"After {dateHappened.Subtract(DateTime.UtcNow).LimitedHumanize(2)}");
                else
                    embed.AddField($"{(warnee as SocketGuildUser).Nickname} aka {warnee.Username} ({warnee.Id}) has manually been un{actType}ed", $"After {DateTime.UtcNow.Subtract(dateHappened).LimitedHumanize(2)}");
                //if (!warnLink.IsNullOrEmpty()) embed.AddField("Jumplink", warnLink);
                embed.WithColor(Color.Green);
                embed.WithCurrentTimestamp();

                channel.SendMessageAsync(embed: embed.Build()).Result.GetJumpUrl();
                return;
            }
            catch (Exception e)
            {
                _ = new LogMessage(LogSeverity.Error, "Logging", "Error", e).Log();
            }
            return;
        }

        public static void LogManualEndTempAct(IGuild guild, ulong userID, string actType, DateTime dateHappened)
        {
            try
            {
                LogSettings settings = guild.LoadFromFile<LogSettings>();
                ITextChannel channel = guild.GetTextChannelAsync(settings?.pubLogChannel ?? settings?.logChannel ?? 0).Result;
                if (channel == null)
                    return;

                var embed = new EmbedBuilder();
                embed.AddField($"{userID} has manually been un{actType}ed", $"After {DateTime.UtcNow.Subtract(dateHappened).LimitedHumanize(2)}");
                //if (!warnLink.IsNullOrEmpty()) embed.AddField("Jumplink", warnLink);
                embed.WithColor(Color.Green);
                embed.WithCurrentTimestamp();

                channel.SendMessageAsync(embed: embed.Build()).Result.GetJumpUrl();
                return;
            }
            catch (Exception e)
            {
                _ = new LogMessage(LogSeverity.Error, "Logging", "Error", e).Log();
            }
            return;
        }

        private async Task HandleDelete(Cacheable<IMessage, ulong> message, ISocketMessageChannel channel)
        {
            _ = LogDelete(message, channel);
        }

        private async Task LogDelete(Cacheable<IMessage, ulong> message, ISocketMessageChannel channel)
        {
            try
            {
                if (!(channel is SocketGuildChannel)) return;
                LogMessage("Deleted message", message.GetOrDownloadAsync().Result);
            }
            catch (Exception exception)
            {
                await new LogMessage(LogSeverity.Error, "Logging", "Error", exception).Log();
            }
            //Console.WriteLine(new LogMessage(LogSeverity.Info, "Logging", "Message deleted"));
        }
    }
}