﻿using System.Collections.Generic;
using System.Text.RegularExpressions;
using TrumpBot.Models;

namespace TrumpBot.Modules.Commands
{
    internal interface ICommand
    {
        string CommandName { get; }
        List<Regex> Patterns { get; set; }
        Command.CommandPriority Priority { get; set; }
        bool HideFromHelp { get; set; }
        string HelpDescription { get; set; }
        
        List<string> RunCommand(ChannelMessageEventDataModel messageEvent, GroupCollection arguments = null, bool useCache = true);
    }
}
