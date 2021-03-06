﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using TwitchBot.JSON_Objects;
using TwitchBot.ChatCommands;
using JsonConfig;
using JsonFx;
using Newtonsoft.Json.Linq;
using TwitchBot.ChatCommands.Giveaway;
using TwitchBot.Forms;

namespace TwitchBot {

	public partial class Bot : Form {
		//private Dictionary<string, double> cooldownList = new Dictionary<string, double>();
		//private Dictionary<string, string> commandList = new Dictionary<string, string>();

		private const string LOG_PREFIX = "[TKMB]";

		public Bot() {
			InitializeComponent();
		}

		private TwitchIrcConnection channelConnection;
		private readonly List<IChatCommand> chatCommands = new List<IChatCommand>();

		//Think of a good way of setting up commands...
		private void SetupCommands() {
			//Load existing commands
			CommandManager.LoadCommands();

			//Sort commands so that admin commands or the most important ones come first.
			for(var i = 0; i<chatCommands.Count; i++) {
				//Prevent casting twice
				var itm = chatCommands[i];
				var adminCmd = itm as IAdminChatCommand;
				if (adminCmd != null && adminCmd.PrioritiseCommands){
					chatCommands.Remove(itm);
					chatCommands.Insert(0, itm);
				}
			}

			//Add commands to list in the CommandManager
			foreach(var itm in chatCommands) {
				foreach(var cmd in itm.Commands) {
					//If the command isn't already in the list add it to the list
					if(CommandManager.commands.All(f => f.Name != cmd[0])) {
						CommandManager.AddCommand(new CommandData { Name = cmd[0], Enabled = true, Description = cmd[1], Privileges = cmd[2].Split(' ') });
					}
				}
			}

			//Update list of commands
			CommandManager.UpdateCommandList();
		}

		private void Bot_Load(object sender, EventArgs e) {
			Program.GiveawayPointsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "points.txt");
			Program.LoginSettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "login.txt");
			Program.SettingsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

			//Setup connections to IRC servers
			ChatSettings.Channel = txtChannel.Text;

			//Add commands to List of chat commands
			//TODO: Add the majority of these commands inside of the XML file (maybe store it in resources then create the file if it doesn't already exist?)
			chatCommands.Add(new AdminCommands());
			chatCommands.Add(new DefaultChatCommands());
			chatCommands.Add(new SongCommands());
			chatCommands.Add(new GiveawayCommands());

			//Setup prioritisation and add commands from the list into the listview
			SetupCommands();

			if(File.Exists(settingsFile)) {
				//settings.LoadSettings(settingsFile);
				LoadSettings();
			}

			//Setup connection to normal chat
			channelConnection = new TwitchIrcConnection("irc.twitch.tv", txtChannel.Text, txtBotUsername.Text, txtBotPassword.Text);
			//Setup connection to be able to whisper to users - no longer needed as twitch integrated this into their main IRC channel.
			//whisperConnection = new TwitchIrcConnection("199.9.253.119", txtChannel.Text, txtBotUsername.Text, txtBotPassword.Text);

			giveawayAddPointsTimer = new Timer {
				Interval = 60 * 1000,
				Enabled = true
			};
			giveawayAddPointsTimer.Tick += AddGiveawayPoints;

			if(chkboxAutoStart.Checked) {
				cmdStartBot.PerformClick();
			}

			channelConnection.ReceiveData += ParseReceivedData;
		}


		private bool botEnabledSwitch;

		private void cmdStartBot_Click(object sender, EventArgs e) {
			if(botEnabledSwitch) {
				botEnabledSwitch = false;
				channelConnection.Disconnect();
				cmdStartBot.Text = "Start";
			} else {
				botEnabledSwitch = true;
				channelConnection.ConnectToServer();
				cmdStartBot.Text = "Stop";
			}
		}

		private readonly string settingsFile = Application.StartupPath + @"\settings.conf";
		#region "Save/Load Settings"

		//This seems icky to me, is it?
		//Defaults to false
		private static bool GetBool(string data) {
			bool tmpBool;
			if(bool.TryParse(data, out tmpBool)) {
				return tmpBool;
			} else {
				return false;
			}
		}

		private void SaveSettings() {
			var jsonObject = JObject.FromObject(new{
				Settings = new{
					BotUsername = txtBotUsername.Text,
					BotPassword = txtBotPassword.Text,
					Channel = txtChannel.Text,

					AutoStartBotOnLoad = chkboxAutoStart.Checked,
					ShouldLogChatMessages = chkboxLogChatMessages.Checked,
					PurgeNonSubLinks = chkboxPurgeNonSubsLinks.Checked,
					WelcomeMessage = txtWelcomeMessage.Text,
					LeavingMessage = txtLeavingMessage.Text,

					GiveawayPointsEarned = txtGiveawayPointsEarnedPerX.Text
				}
			});

			File.WriteAllText(Application.StartupPath + @"\settings.conf", JsonConvert.SerializeObject(jsonObject, Formatting.Indented));
		}

		private void LoadSettings() {
			var config = Config.ApplyJson(File.ReadAllText(Application.StartupPath + @"\settings.conf"));
			Config.SetUserConfig(config);

			if(Config.User) {
				txtBotUsername.Text = Config.User.Settings.BotUsername;
				txtBotPassword.Text = Config.User.Settings.BotPassword;
				txtChannel.Text = Config.User.Settings.Channel;

				//General bot settings
				chkboxAutoStart.Checked = Config.User.Settings.AutoStartBotOnLoad;
				txtWelcomeMessage.Text = Config.User.Settings.WelcomeMessage;
				txtLeavingMessage.Text = Config.User.Settings.LeavingMessage;
				chkboxLogChatMessages.Checked = Config.User.Settings.ShouldLogChatMessages;
				chkboxPurgeNonSubsLinks.Checked = Config.User.Settings.PurgeNonSubLinks;

				//Giveaway settings
				txtGiveawayPointsEarnedPerX.Text = Config.User.Settings.GiveawayPointsEarned;
			}
		}

		private void chkboxAutoStart_CheckedChanged(object sender, EventArgs e) {
			SaveSettings();
		}

		private void btnSaveSettings_Click(object sender, EventArgs e) {
			SaveSettings();
		}

		#endregion "Save/Load Settings"

		//TODO: Work on cooldown system. Implement it into the new command system
		#region "Cooldown system"
		/*
				private string getCooldown(string commandName) {
					if(cooldownList.Count == 0) {
						return "Something went wrong :(";
					} else {
						if(cooldownList.ContainsKey(commandName)) {
							return "Command is on cooldown for " + Math.Round((cooldownList[commandName] - unixTimestamp()), 2) + " seconds.";
						} else {
							return "Something went wrong :(";
						}
					}
				}

				private bool commandOnCooldown(string commandName) {
					//If cooldown is set to blank or 0 then disable cooldown functionality.
					if(txtCooldownTime.Text == 0.ToString() | string.IsNullOrEmpty(txtCooldownTime.Text)) {
						return false;
					}

					//Set timeout to currentTime + cooldownTime
					double timeoutEnd = unixTimestamp() + Convert.ToDouble(txtCooldownTime.Text);
					if(cooldownList.Count == 0) {
						//Nothing in cooldown list, set command on cooldown.
						cooldownList.Add(commandName, timeoutEnd);
						return false;
					} else {
						if(cooldownList.ContainsKey(commandName)) {
							//Command is in the cooldown list,
							if(unixTimestamp() >= cooldownList[commandName]) {
								//Command has gone off cooldown, remove from list and report back.
								cooldownList.Remove(commandName);
								return false;
							} else {
								//Command still on cooldown
								return true;
							}
						} else {
							//Command not on cooldown, add it to the list and let the command run once.
							cooldownList.Add(commandName, timeoutEnd);
							return false;
						}
					}
				}
				*/
		#endregion "Cooldown system"

		private static double UnixTimestamp() {
			return (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalSeconds;
		}

		public void SetupTooltips() {
			var toolTip1 = new ToolTip() {
				AutoPopDelay = 5000,
				InitialDelay = 500,
				ReshowDelay = 500,
				ShowAlways = true
			};

			toolTip1.SetToolTip(txtChannel, "Enter your twitch channel here, example: ManselD or #ManselD");
			toolTip1.SetToolTip(txtCooldownTime, "Enter the delay (in seconds) between each command entered. If 0 is entered then there'll be no cooldown.");
			toolTip1.SetToolTip(txtWelcomeMessage, "Enter the message that you want the bot to say when it enters the chat.");
			toolTip1.SetToolTip(txtLeavingMessage, "Enter the message that you want the bot to say when it leaves the chat.");
		}

		public StreamData streamData = new StreamData();

		public void ParseReceivedData(object sendingClass, string message) {
			var arr = message.Split(' ');

			if(arr[2] == "PRIVMSG") {
				var sender = channelConnection.get_sender(message);
				var weirdString = channelConnection.get_channel(true) + " :";
				//StringComparison.Ordinal is good for comparing English words and provides better speed efficiency.
				var userMessage = message.Substring(message.IndexOf(weirdString, StringComparison.Ordinal));
				userMessage = userMessage.Remove(0, weirdString.Length);
				var msg = userMessage.Split(' ');

				ProcessReceivedData(sender, msg);
			}
		}

		public void ProcessReceivedData(TwitchUser sender, string[] message) {
			//Only log messages if the checkbox is ticked.
			if(chkboxLogChatMessages.Checked) {
				LogMessage(sender.Username + ": " + string.Join(" ", message));
			}

			//Purge non-sub links (Moderator exception)
			if(chkboxPurgeNonSubsLinks.Checked) {
				if(channelConnection.get_channel(false) != sender.Username.ToLower()) {
					if(sender.UserType != "mod") {
						if(!sender.Sub) {
							foreach(var potentialUrl in message) {
								var lines = Properties.Resources.TLDS.Split(new string[1] { Environment.NewLine }, StringSplitOptions.None);
								var illegal = false;

								foreach(var line in lines) {
									if(line != null) {
										if(potentialUrl.Contains("." + line.ToLower() + "?")) {
											illegal = true;
											break;
										} else if(potentialUrl.Contains("." + line.ToLower() + "/")) {
											illegal = true;
											break;
										} else if(potentialUrl.Contains("." + line.ToLower())) {
											illegal = true;
											break;
										}
									}
								}

								if(illegal) {
									channelConnection.send_message("/timeout " + sender.Username + " 1");
									channelConnection.send_message("Naughty " + sender.Username + ", only subs can link!");
									break;
								}
							}
						}
					}
				}
			}

			if(message[0] == "!help" || message[0] == "!commands") {
				var sb = new StringBuilder();
				sb.Append("Here is a full list of commands: ");
				foreach(var processor in chatCommands) {
					foreach(var cmd in processor.Commands) {
						sb.Append(cmd[0] + ", ");
					}
				}

				var completeData = sb.ToString().Trim();
				completeData = completeData.Remove(completeData.Length - 1);
				channelConnection.send_message(completeData);
				return;
			}

			//If it's an admin command and bypassEnabledStatus is true, process command regardless of the bot's enabled status.
			//Otherwise, respect the status.

			//First check if the command exists (instead of looping through all of the command processors - wasting time)
			var curCommand = CommandManager.GetCommandData(message[0]);
			if(curCommand != null) {
				if(curCommand.Enabled) {
					if(string.IsNullOrEmpty(curCommand.CustomReply)) {
						foreach(var processor in chatCommands) {
							bool sendViaChat;
							string returnMessage;
							processor.ProcessCommand(sender, message, out sendViaChat, out returnMessage);
							if(returnMessage != null) {
								var adminCmd = processor as IAdminChatCommand;
								if(adminCmd != null) {
									if(adminCmd.BypassEnabledStatus) {
										ProcessCommandData(returnMessage);
										break;
									}
								} else {
									if(ChatSettings.BotEnabled) {
										ProcessCommandData(returnMessage);
										break;
									}
								}

								return;
							}
						}
					} else {
						//Process custom command
						ProcessCommandData(curCommand);
					}
				}
			}
		}

		private void ProcessCommandData(string message) => channelConnection.send_message(message);

		private void ProcessCommandData(CommandData command) {
			if(command != null) {
				var messageToSend = command.CustomReply;
				foreach(var variable in command.Variables) {
					var replaceStr = "{" + variable.Name + (variable.Increment ? "++" : "--") + "}";
                    if(variable.Increment) {	
						++variable.Value;
					} else {
						--variable.Value;
					}

					messageToSend = messageToSend.Replace(replaceStr, variable.Value.ToString());
				}

				//Use the command data in order to generate a custom reply
				channelConnection.send_message(messageToSend);
			}

			CommandManager.SaveCommand(command);
			CommandManager.AddCommand(command);
		}

		#region "Giveaway"

		private delegate void AddGiveawayPointsInvoker(object s, EventArgs e);

		public void AddGiveawayPoints(object s, EventArgs e) {
			if(InvokeRequired) {
				Invoke(new AddGiveawayPointsInvoker(AddGiveawayPoints), s, e);
			} else {
				try {
					var twitchDl = new WebClient();
					var data = twitchDl.DownloadString("http://tmi.twitch.tv/group/user/" + channelConnection.get_channel() + "/chatters");
					var json = JsonConvert.DeserializeObject<Misc.GiveawayTwitchData>(data);
					//Add points to the moderators
					foreach(string user in json.Chatters.Moderators) {
						Misc.AddPoints(user, Convert.ToInt64(txtGiveawayPointsEarnedPerX.Text));
					}

					//Add points to the viewers
					foreach(var user in json.Chatters.Viewers) {
						Misc.AddPoints(user, Convert.ToInt64(txtGiveawayPointsEarnedPerX.Text));
					}
				} catch(Exception) {
					//MessageBox.Show(ex.ToString());
				}
			}
		}

		public bool giveawayActive;
		private Timer giveawayAddPointsTimer;
		private Timer giveawayTimer;
		private Timer giveawayLengthProgressTimer;
		private double giveawayFinishTime;
		private double giveawayTotalLength;

		private void cmdGiveawayStart_Click(object sender, EventArgs e) {
			if(!giveawayActive && channelConnection.Users.Count > 1) {
				//If a giveaway isn't currently hapenning.
				giveawayTotalLength = Convert.ToInt16(txtGiveawayLength.Text) * 60;
				giveawayFinishTime = UnixTimestamp() + giveawayTotalLength;
				lblGiveawayLengthProgress.Visible = true;

				giveawayActive = true;
				giveawayTimer = new Timer{
					Interval = Convert.ToInt16(txtGiveawayLength.Text)*1000*60,
					Enabled = true
				};
				giveawayTimer.Tick += GiveawayTimerFinished;

				//Report back on progress every second.
				giveawayLengthProgressTimer = new Timer{
					Interval = 1000,
					Enabled = true
				};
				giveawayLengthProgressTimer.Tick += GiveawayLengthProgressReport;

				lblGiveawayProgress.Text = @"A giveaway has been started. Entered users: " + giveawayEnteredUsers.Items.Count + @".";
				channelConnection.send_message("A giveaway has started for " + txtGiveawayAmountOfGiveawayItems.Text + " item(s). This will last " + txtGiveawayLength.Text + " minutes. If you have " + txtGiveawayPointsRequired.Text + " points type !enter to enter the giveaway.");
			}
		}

		public delegate void AddGiveawayUserToListInvoker(string user);
		public void AddGiveawayUserToList(string user) {
			if(InvokeRequired) {
				Invoke(new AddGiveawayUserToListInvoker(AddGiveawayUserToList), user);
			} else {
				giveawayEnteredUsers.Items.Add(user);
				if(giveawayEnteredUsers.Items.Count >= 1) {
					lblGiveawayProgress.Text = @"A giveaway has been started. Entered users: " + giveawayEnteredUsers.Items.Count + @".";
				}
			}
		}

		private delegate void GiveawayTimerFinishedInvoke(object s, EventArgs e);

		private readonly List<string> giveawayWinners = new List<string>();

		private void GiveawayTimerFinished(object s, EventArgs e) {
			if(InvokeRequired) {
				Invoke(new GiveawayTimerFinishedInvoke(GiveawayTimerFinished), s, e);
			} else {
				if(giveawayActive) {
					if(channelConnection.Users.Count > 1) {
						//Determine winner
						LogMessage("Giveaway has just finished at " + DateTime.Now);
						giveawayActive = false;
						giveawayTimer.Enabled = false;
						giveawayLengthProgressTimer.Enabled = false;

						if(giveawayEnteredUsers.Items.Count >= 1) {
							var giveawayItems = Convert.ToInt32(txtGiveawayAmountOfGiveawayItems.Text);
							var rand = new Random();
							for(var i = 0; i < giveawayItems; i++) {
								var user = giveawayEnteredUsers.Items[rand.Next(0, giveawayEnteredUsers.Items.Count)].ToString();
								while(giveawayWinners.Contains(user)) {
									user = giveawayEnteredUsers.Items[rand.Next(0, giveawayEnteredUsers.Items.Count)].ToString();
								}

								giveawayWinners.Add(user);
							}

							if(giveawayWinners.Count > 1) {
								var winnerMsg = "The giveaway has finished. The winners are... ";
								foreach(var winner in giveawayWinners) {
									//If it's the last winner, don't add a comma at the end, add a period.
									if(winner == giveawayWinners[giveawayWinners.Count - 1]) {
										winnerMsg += " and " + winner + ".";
									} else {
										winnerMsg += " " + winner + ",";
									}
								}
								channelConnection.send_message(winnerMsg + " Congratulations, the winners will get their prize(s) shortly.");
								lblGiveawayProgress.Text = @"A giveaway has finished, the winners were: " + winnerMsg;
								channelConnection.send_message("/w " + channelConnection.get_channel() + " A giveaway has finished, the winners were: " + winnerMsg);
							} else {
								channelConnection.send_message("The giveaway has finished. The winner is " + giveawayWinners[0] + ", congratulations! You will get your prize(s) soon!");
								lblGiveawayProgress.Text = @"A giveaway has finished, the winner was: " + giveawayWinners[0];
								channelConnection.send_message("/w " + channelConnection.get_channel() + " A giveaway has finished, the winner was: " + giveawayWinners[0]);
							}
						} else {
							channelConnection.send_message("Not enough people entered the giveaway so there was no winner.");
							lblGiveawayProgress.Text = @"A giveaway has finished with no winner.";
						}

						//Hide giveaway length
						lblGiveawayLengthProgress.Visible = false;

						//Clear entered users and winners list.
						giveawayEnteredUsers.Items.Clear();
						giveawayWinners.Clear();
					} else {
						channelConnection.send_message("Not enough people entered the giveaway so there was no winner.");
						lblGiveawayProgress.Text = @"A giveaway has finished with no winner.";
					}
				}
			}
		}

		private delegate void GiveawayLengthProgressReportInvoke(object s, EventArgs e);

		private void GiveawayLengthProgressReport(object s, EventArgs e) {
			if(InvokeRequired) {
				Invoke(new GiveawayLengthProgressReportInvoke(GiveawayLengthProgressReport), s, e);
			} else {
				var curTimeLeft = Math.Round(giveawayFinishTime - UnixTimestamp());
				lblGiveawayLengthProgress.Text = curTimeLeft + @" seconds left on giveaway.";
			}
		}

		#endregion "Giveaway"

		#region "Misc"

		private delegate void AddLogMessageInvoker(string msg);

		public void LogMessage(string msg) {
			if(this.InvokeRequired) {
				this.Invoke(new AddLogMessageInvoker(LogMessage), msg);
			} else {
				var logText = DateTime.Now.Hour + ":" + DateTime.Now.Minute + " " + msg;
				logs.Items.Add(logText);
			}
		}

		private void txtCommand_KeyDown(object sender, KeyEventArgs e) {
			if(e.KeyCode == Keys.Enter) {
				cmdSendCommand.PerformClick();
			}
		}

		private void cmdSendCommand_Click(object sender, EventArgs e) {
			if(txtCommand.Text != "") {
				var user = new TwitchUser("ManselD", "", true);
				Program.BotForm.ProcessReceivedData(user, txtCommand.Text.Split(' '));
				channelConnection.send_message(txtCommand.Text);
				txtCommand.Clear();
            }
		}

		private void chkboxLogChatMessages_CheckedChanged(object sender, EventArgs e) {
			if(chkboxLogChatMessages.Checked) {
				LogMessage(LOG_PREFIX + " Chat logging has been enabled.");
			} else {
				LogMessage(LOG_PREFIX + " Chat logging has been disabled.");
			}
		}

		#endregion "Misc"

		private void addCommandToolStripMenuItem_Click(object sender, EventArgs e) {
			var frm = new FrmAddCommand();
			frm.Show();
		}

		private void editToolStripMenuItem_Click(object sender, EventArgs e) {
			if(lstViewCommands.SelectedItems.Count > 0) {
				var frm = new FrmEditCommand();
				frm.Show();

				var lvi = lstViewCommands.SelectedItems[0];

				//Command, enabled, description, privileges
				frm.txtCommand.Text = lvi.Text;
				frm.chkEnabled.Checked = CommandManager.YesNoToBool(lvi.SubItems[1].Text);
				frm.txtDescription.Text = lvi.SubItems[2].Text;
				frm.txtPrivileges.Text = lvi.SubItems[3].Text;
			}
		}

		private void enableDisableToolStripMenuItem_Click(object sender, EventArgs e) {
			if(lstViewCommands.SelectedItems.Count > 1) {
				foreach(ListViewItem itm in lstViewCommands.SelectedItems) {
					SwapCommandEnabledStatus(itm);
                }
			} else {
				SwapCommandEnabledStatus(lstViewCommands.SelectedItems[0]);
			}

			CommandManager.SaveCommands();
        }

		private static void SwapCommandEnabledStatus(ListViewItem itm) {
			//Swap the value of its current status (Yes = No, No = Yes)
			itm.SubItems[1].Text = itm.SubItems[1].Text == "Yes" ? "No" : "Yes";
			var curCmd = CommandManager.GetCommandData(itm.SubItems[0].Text);
			curCmd.Enabled = CommandManager.YesNoToBool(itm.SubItems[1].Text);
			CommandManager.AddCommand(curCmd);
		}

		private void removeCommandToolStripMenuItem_Click(object sender, EventArgs e) {
			if(lstViewCommands.SelectedItems.Count > 1) {
				foreach(ListViewItem itm in lstViewCommands.SelectedItems) {
					CommandManager.RemoveCommand(itm.SubItems[0].Text);
				}
			} else if(lstViewCommands.SelectedItems.Count == 1) {
				CommandManager.RemoveCommand(lstViewCommands.SelectedItems[0].SubItems[0].Text);
			}

			CommandManager.UpdateCommandList();
			CommandManager.SaveCommands();
		}
	}
}