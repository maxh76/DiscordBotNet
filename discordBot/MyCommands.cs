﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.API;
using System.Threading.Tasks;

namespace discordBot
{
    public class RequireBotMod : PreconditionAttribute
    {
        protected readonly static ulong[] BotMods = new ulong[] { 259532984909168659, 212687824816701441 };
        protected bool IsBotMod(IUser user) => BotMods.Contains(user.Id);

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services) =>
            Task.Run(() => IsBotMod(context.User) ?
                PreconditionResult.FromSuccess() :
                PreconditionResult.FromError($"{context.User.Username} is not a bot mod"));
    }

    public abstract class Commands : ModuleBase
    {
        protected readonly static ulong[] BotMods = new ulong[] { 259532984909168659, 212687824816701441 };
        protected bool IsBotMod(IUser user) => BotMods.Contains(user.Id);
    }

    public class TextCommands : Commands
    {
        [Command("tp")]
        public async Task TP([Remainder]string content)
        {
            if (content == null) // Exit if there wasn't an argument
                return;

            // Mentioned channel ids
            var channelID = Context.Message.MentionedChannelIds;
            // Mentioned role ids
            var roleID = Context.Message.MentionedRoleIds;
            // Mentioned member ids
            var memberID = Context.Message.MentionedUserIds;

            // Splits the content by spaces
            string[] splitContent = content.Split(new char[] { ' ' });
            // Gets the last argument
            string lastArg = splitContent.LastOrDefault();
            // Gets the first # or @ in the argument
            //char lastArgType = lastArg.FirstOrDefault(a => a == '#' || a == '@');

            if (string.IsNullOrEmpty(lastArg))
                return;

            // The destination user
            IGuildUser destUser = null;
            // The destination channel
            IVoiceChannel destChannel = null;

            // Attempts to parse the last argument, removing (<@, <#, or <@&) and >, to an id
            if (!ulong.TryParse(lastArg.Remove(lastArg.Length - 1).Remove(0, 2), out ulong lastArgID))
                if (!ulong.TryParse(lastArg.Remove(lastArg.Length - 1).Remove(0, 3), out lastArgID))
                    if (!ulong.TryParse(lastArg, out lastArgID))
                    {
                        await ReplyAsync("Couldn't find the id.");
                        return;
                    }

            // Attempt to find destination user based on lastArgID
            destUser = await Context.Guild.GetUserAsync(lastArgID);
            // Attempt to find destination channel based on lastArgID
            destChannel = await Context.Guild.GetVoiceChannelAsync(lastArgID);

            if (destChannel == null) // If the destination channel is null
            {
                if (destUser == null) // If the destination user is ALSO null, exit
                {
                    await ReplyAsync("No voice channel or user found");
                    return;
                }
                if (destUser.VoiceChannel == null) // If the destination user is not in a voice channel
                {
                    await ReplyAsync($"Specified user. {destUser.Username}. is not in a voice channel");
                    return;
                }
                // Destination user must be not null and in a voice channel, set their channel to the destination channel
                destChannel = destUser.VoiceChannel;
            }

            // Potential teleport candidates
            var candidates = await Context.Guild.GetUsersAsync();
            // A list of IGuildUser to teleport
            List<IGuildUser> teleport = new List<IGuildUser>();
            // The afk voice channel of the server
            IVoiceChannel afkChannel = await Context.Guild.GetAFKChannelAsync();

            foreach (ulong id in memberID) // Add every mentioned user to the teleport list
            {
                teleport.Add(await Context.Guild.GetUserAsync(id));
            }

            if (splitContent.Length == 1) // If there was only one argument, add the author to the teleport list
                teleport.Add(Context.Message.Author as IGuildUser);

            if (IsBotMod(Context.Message.Author)) // If the author is a bot mod
            {
                if (splitContent.Length == 1 && destUser==null) // If there wasn't a user specified and there was only one argument, teleport everyone in voice
                    foreach (IGuildUser user in candidates.Where(a => !string.IsNullOrEmpty(a.VoiceSessionId) && a.VoiceChannel != afkChannel))
                        teleport.Add(user);

                if (roleID.Count >= 0) // If a role was specified, teleport every user with specified role who is in voice
                    foreach (ulong id in roleID)
                        foreach (IGuildUser user in candidates.Where(a => !string.IsNullOrEmpty(a.VoiceSessionId) && a.RoleIds.Any(b => b == id)))
                            teleport.Add(user);
            }
            // Removes duplicates
            teleport = teleport.Distinct().ToList();

            // Removes the destination user and any user in the afk channel from the teleport list
            teleport.RemoveAll(a => a == destUser || a.VoiceChannel == afkChannel || a.VoiceChannel == destChannel);
            
            foreach (var user in teleport) // Teleport every user in the list
            {
                Console.WriteLine($"Teleporting {user.Username} to {destChannel.Name}");
                await user.ModifyAsync(a => a.Channel = new Optional<IVoiceChannel>(destChannel));
            }
            await ReplyAsync($"Teleported {teleport.Count} users to {destChannel.Name}");
        }
    }

    [Group("debug")]
    [RequireBotMod()]
    public class DebugCommands : Commands
    {
        [Command("roles")]
        public async Task GetRoles()
        {
            IGuild g = Context.Guild;
            if (g == null) { return; }
            await ReplyAsync(
                "Roles:\n" +
                g.Roles.OrderByDescending(p => p.Position).
                    Aggregate<IRole, string>(string.Empty, (a, b) => (a += $"{b.Position} {b.Name}: {b.Id}\n")));
        }

        [Command("echo")]
        public async Task Echo([Remainder]string input)
        {
            await ReplyAsync(input);
        }

        [Command("exit")]
        public async Task Exit()
        {
            await Context.Client.StopAsync();
            await Exit();
        }
    }
}
