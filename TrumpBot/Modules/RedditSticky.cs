﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using Meebey.SmartIrc4net;
using SharpRaven;
using SharpRaven.Data;
using TrumpBot.Configs;
using TrumpBot.Models;
using TrumpBot.Models.Config;
using TrumpBot.Services;

namespace TrumpBot.Modules
{
    internal class RedditSticky : IDisposable
    {
        private IrcClient _ircClient;
        private RedditStickyConfigModel _config;
        private List<string> _checkedThings;
        private Reddit _reddit;
        private Thread _thread;
        private ILog _log = LogManager.GetLogger(typeof(RedditSticky));
        private RavenClient _ravenClient = Raven.GetRavenClient();
        private IrcBot _ircBot;

        private Regex _twitterRegex = new Regex(@"https?:\/\/twitter\.com\/(?:\#!\/)?(\w+)\/(?:status|statuses)\/(\d+)",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

        internal RedditSticky(IrcClient client, IrcBot ircBot)
        {
            _ircClient = client;
            _ircBot = ircBot;
            LoadConfig();
            _checkedThings = LoadStoredThings();
            _reddit = new Reddit();
            if (!_config.Enabled)
            {
                // Sick of getting spammed e-mails
                //_ravenClient?.Capture(new SentryEvent("RedditSticky not enabled") {Level = ErrorLevel.Info});
                _log.Info("RedditSticky not enabled");
                return;
            }
            _log.Debug("Creating RedditSticky thread");
            CreateThread();
        }

        public virtual void Dispose()
        {
            Stop();
        }

        internal void LoadConfig()
        {
            _config = ConfigHelpers.LoadConfig<RedditStickyConfigModel>(ConfigHelpers.ConfigPaths.RedditStickyConfig);
        }

        internal void Start()
        {
            if (!_thread.IsAlive)
            {
                CreateThread();
            }
        }

        internal void Stop()
        {
            if (_thread.IsAlive)
            {
                _thread.Abort();
            }
        }

        internal bool IsAlive()
        {
            return _thread != null && _thread.IsAlive;
        }

        private void CreateThread()
        {
            Thread newThread = new Thread(() => CheckSubreddit());
            _thread = newThread;
            newThread.Start();
            _log.Debug("RedditSticky thread created");
        }

        private List<string> LoadStoredThings()
        {
            return ConfigHelpers.LoadConfig<List<string>>(ConfigHelpers.ConfigPaths.RedditThingsCache);
        }

        private void SaveStoredThnigs(List<string> things)
        {
            ConfigHelpers.SaveConfig(things, ConfigHelpers.ConfigPaths.RedditThingsCache);
        }

        private void CheckSubreddit()
        {
            while (true)
            {
                Thread.Sleep(_config.CheckInterval * 1000);
                RedditModel.SubredditModel.Subreddit subreddit;
                string target = _config.Subreddit;
                _log.Debug($"Checking subreddit, targeting {target}");

                try
                {
                    subreddit = _reddit.GetSubreddit(target, false);
                }
                catch (Http.HttpException e)
                {
                    _log.Debug($"Got HTTP exception: {e.Message}");
                    _ravenClient?.Capture(new SentryEvent(e));
                    continue;
                }
                catch (Exception e)
                {
                    _log.Debug($"Got some other exception when attempting to hit the Reddit API: {e.Message}\r\nStacktrace follows");
                    _log.Debug(e);
                    _ravenClient?.Capture(new SentryEvent(e));
                    continue;
                }
                
                foreach (RedditModel.SubredditModel.SubredditChildren child in subreddit.Data.Children)
                {
                    if (child.Thread.IsSticky)
                    {
                        if (!_checkedThings.Contains(child.Thread.Name))
                        {
                            _log.Debug($"Found sticky to broadcast to channels, ID is: {child.Thread.Name}");
                            var match = _twitterRegex.Match(child.Thread.Url.AbsoluteUri);
                            if (match.Success)
                            {
                                _log.Debug("Looks like this is a Twitter URL");
                                long tweetId = 0;
                                try
                                {
                                    tweetId = long.Parse(match.Groups[2].Value);
                                }
                                catch (FormatException e)
                                {
                                    _log.Debug(e);
                                    _ravenClient?.Capture(new SentryEvent(e));
                                    continue;
                                }
                                if (tweetId == _ircBot.TwitterStream?.LastTrumpTweetId && _config.IgnoreTrumpTweetReposts)
                                {
                                    _log.Debug("Tweet is a repost of the most recent Trump tweet, ignoring.");
                                    continue;
                                }

                            }
                            foreach (string channel in _config.Channels)
                            {
                                if (_ircClient.JoinedChannels.Contains(channel))
                                {
                                    string message = $"{WebUtility.HtmlDecode(child.Thread.Title)} ({child.Thread.Domain}) by {child.Thread.Author}";
                                    if (child.Thread.AuthorFlairText != null)
                                        message += $" ({child.Thread.AuthorFlairText})";
                                    if (child.Thread.IsVideo)
                                    {
                                        message +=
                                            $", link: https://old.reddit.com{child.Thread.Permalink}";
                                    }
                                    else
                                    {
                                        message +=
                                            $", link: {WebUtility.UrlDecode(child.Thread.Url.AbsoluteUri).Replace("&amp;", "&")}";
                                    }

                                    _ircClient.SendMessage(SendType.Message, channel, message);
                                }
                            }
                            _checkedThings.Add(child.Thread.Name);
                            SaveStoredThnigs(_checkedThings);
                        }
                    }
                }
            }

        }
    }
}
