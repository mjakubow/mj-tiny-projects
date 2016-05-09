/*
 * Copyright (c) 2006, Second Life Reverse Engineering Team
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the Second Life Reverse Engineering Team nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */

//-----------------------------------------------------------------------
// <copyright file="AnimationBot.cs" company="MHJ">
//   Copyright (c) 2015 MHJ.  All rights reserved.
// </copyright>
//
// <summary>
//
//   This file implements a simple NPC (non-player character, or bot)
//   using the libopenmetaverse library.  This bot's purpose is to
//   facilitate usage of animation objects (e.g., pose balls) in Second
//   Life without the need to use multiple graphical browser windows
//   (which severely degrade performance).
//
//   Much of this code is copied and adapted from the libopenmetaverse
//   test client, using a minimal set of selected features.  Some
//   necessary additional code is included to enable usage of animation
//   objects.
//
// </summary>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using OpenMetaverse;
using OpenMetaverse.Packets;
using System.IO;


namespace OpenMetaverse.AnimationBot
{
  /// <summary>
  /// Generic utility functions
  /// </summary>
  static class Tools
  {
    /// <summary>
    /// Logging support
    /// </summary>
    public static void Log(
      string msg)
    {
      Console.Write(msg);
    }

    public static void LogInfo(
      string msg)
    {
      Console.Write("INFO - " + msg);
    }

    public static void LogTime(
      string strMsg)
    {
      Console.Write("[" + Tools.CurrentDateTime + "] " + strMsg);
    }

    public static void LogWarn(
      string msg)
    {
      Console.WriteLine("WARNING - " + msg);
    }

    public static void Quit(
      int errorCode,
      string strMsg)
    {
      Console.WriteLine("\nERROR: " + strMsg + "\n");
      System.Environment.Exit(errorCode);
    }

    /// <summary>
    /// Return current date-time string.
    /// </summary>
    public static string CurrentDateTime
    {
      get { return System.DateTime.Now.ToLocalTime().ToString(); }
    }

    /// <summary>
    /// Return command-line executable name.
    /// </summary>
    public static string ExecutableName
    {
      get { return Environment.CommandLine.Split(' ')[0]; }
    }

  } // class Tools


  /// <summary>
  /// Implementation of animation bots (NPCs, or programmatically operated avatars)
  /// </summary>
  class AnimationBotManager
  {
    /// <summary>
    /// Main client object for interfacing with OpenMetaverse worlds
    /// </summary>
    GridClient gridClient;
    public GridClient Client { get { return gridClient; } }

    /// <summary>
    /// Parameters for a single bot account
    /// </summary>
    string strFirstName, strLastName, strPassword;

    /// <summary>
    /// Parameters for finding nearby objects in sim
    /// </summary>
    Dictionary<UUID, Primitive> primsAwaitingProperties = new Dictionary<UUID, Primitive>();
    AutoResetEvent propertiesReceivedEvent = new AutoResetEvent(false);

    /// <summary>
    /// Parameters for IM sending
    /// </summary>
    string strImRecipientName = String.Empty;
    ManualResetEvent avatarNameSearchEvent = new ManualResetEvent(false);
    Dictionary<string, UUID> mapAvatarNameToKey = new Dictionary<string, UUID>();

    /// <summary>
    /// Timer for periodic operations
    /// </summary>
    System.Timers.Timer periodicActionsTimer;
    const int periodActionsUpdatePeriod = 500; // 0.5 seconds

    /// <summary>
    /// Class version
    /// </summary>
    public static string Version
    {
      get { return "0.0.0.0"; }
    }


    /// <summary>
    /// Constructor to initialize a single bot account
    /// </summary>
    /// <param name="strFirstName"></param>
    /// <param name="strLastName"></param>
    /// <param name="strPassword"></param>
    public AnimationBotManager(
      string strFirstName,
      string strLastName,
      string strPassword)
    {
      this.strFirstName = strFirstName;
      this.strLastName = strLastName;
      this.strPassword = strPassword;
    }

    /// <summary>
    /// Log in a bot into the default grid (Second Life).
    /// </summary>
    public bool
    LogIn()
    {
      // Create a client for accessing Second Life and OpenSim servers (sims).  Collectively,
      // these sims comprise the grid, which is the basis of each respective virtual world.
      gridClient = new GridClient();

      // Set callbacks for receiving selected data from server.
      gridClient.Objects.ObjectProperties += new EventHandler<ObjectPropertiesEventArgs>(ObjectPropertiesCallback);
      gridClient.Avatars.AvatarPickerReply += new EventHandler<AvatarPickerReplyEventArgs>(AvatarUuidCallback);

      // Try to log in.
      Tools.LogInfo("Logging in " + strFirstName + " " + strLastName + "...\n");
      if (!gridClient.Network.Login(strFirstName, strLastName, strPassword, "AnimationBot", "1.0"))
      {
        // Login failed.
        Tools.LogWarn("Login failure:\n" + gridClient.Network.LoginMessage);
        return false;
      }

      // Set callbacks to handle selected session events.
      gridClient.Self.IM += new EventHandler<InstantMessageEventArgs>(InstantMessageCallback);
      gridClient.Self.ScriptQuestion += new EventHandler<ScriptQuestionEventArgs>(ScriptQuestionCallback);
      gridClient.Inventory.InventoryObjectOffered += new EventHandler<InventoryObjectOfferedEventArgs>(InventoryObjectOfferedCallback);

      // Enable handling of autopilot alerts.
      gridClient.Network.RegisterCallback(PacketType.AlertMessage, AlertMessageHandler);

      // Enable periodic actions.
      periodicActionsTimer = new System.Timers.Timer(periodActionsUpdatePeriod);
      periodicActionsTimer.Elapsed += new System.Timers.ElapsedEventHandler(PeriodicActionsTimerElapsedHandler);
      periodicActionsTimer.Start();

      return true;
    }

    /// <summary>
    /// Log out the currently operating bot.
    /// </summary>
    public void
    LogOut()
    {
      gridClient.Network.Logout();
    }

    /// <summary>
    /// Callback: Accept offered inventory item.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    InventoryObjectOfferedCallback(
      object sender,
      InventoryObjectOfferedEventArgs e)
    {
      Tools.LogInfo("Accepting offered inventory item\n");

      // Accept offered items automatically (default is false).
      e.Accept = true;
    }

    /// <summary>
    /// Callback: Provide a response to selected questions asked by in-world scripts.  This is
    /// mainly for approving avatar animation requests sent by animation objects (e.g., pose
    /// balls).
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    ScriptQuestionCallback(
      object sender,
      ScriptQuestionEventArgs e)
    {
      Tools.LogInfo("Received script question(s)\n");
      Tools.LogInfo("Requestor object=" + e.ItemID.ToString() + ", name=" + e.ObjectName + ", owner=" + e.ObjectOwnerName + "\n");
      Tools.LogInfo("Question(s)=" + e.Questions.ToString() + "\n");

      // Confirm request to animate avatar.
      if (e.Questions == ScriptPermission.TriggerAnimation)
      {
        Tools.LogInfo("Approving animation request\n");
        gridClient.Self.ScriptQuestionReply(e.Simulator, e.ItemID, e.TaskID, ScriptPermission.TriggerAnimation);
      }
    }

    /// <summary>
    /// Callback: Handle IMs sent to the bot, including private, group and object messages.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    InstantMessageCallback(
      object sender,
      InstantMessageEventArgs e)
    {
      Tools.LogInfo(String.Format("{0}: ({1}) {2}: {3} (@{4}:{5})\n",
        e.IM.GroupIM ? "Group IM" : "IM",
        e.IM.Dialog,
        e.IM.FromAgentName,
        e.IM.Message,
        e.IM.RegionID, e.IM.Position));

      // For now, accept a teleport request from anyone.
      // TODO: Include some sort of authentication.
      if (e.IM.Dialog == InstantMessageDialog.RequestTeleport)
      {
        Tools.LogInfo("Accepting teleport request\n");
        gridClient.Self.TeleportLureRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
      }
    }

    /// <summary>
    /// Send a private IM to an avatar.
    /// </summary>
    /// <param name="argumentList"></param>
    public void
    SendInstantMessage(
      List<string> argumentList)
    {
      if (argumentList.Count < 3)
      {
        Tools.Log("Usage: im [first name] [last name] [message]\n");
        return;
      }

      strImRecipientName = (argumentList[0] + " " + argumentList[1]).ToLower();

      // Build the IM.
      string strMessage = argumentList[2];
      for (int i = 3; i < argumentList.Count; i++)
        strMessage += (" " + argumentList[i]);

      if (strMessage.Length > 1023)
        strMessage = strMessage.Remove(1023);

      // Check if we have the avatar's UUID cached.
      if (!mapAvatarNameToKey.ContainsKey(strImRecipientName))
      {
        // Send a query for the recipient's UUID.
        gridClient.Avatars.RequestAvatarNameSearch(strImRecipientName, UUID.Random());
        avatarNameSearchEvent.WaitOne(6000, false);
      }

      // Send the IM if we have the avatar's UUID.
      if (mapAvatarNameToKey.ContainsKey(strImRecipientName))
      {
        UUID imRecipientUuid = mapAvatarNameToKey[strImRecipientName];
        gridClient.Self.InstantMessage(imRecipientUuid, strMessage);
        Tools.LogInfo("Sent IM to " + imRecipientUuid.ToString() + "\n");
      }
      else
        Tools.LogWarn("Name lookup for IM recipient " + strImRecipientName + " failed");
    }
    
    /// <summary>
    /// Callback: Receive an avatar's UUID.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    AvatarUuidCallback(
      object sender,
      AvatarPickerReplyEventArgs e)
    {
      foreach (KeyValuePair<UUID, string> p in e.Avatars)
      {
        string strReceivedName = p.Value.ToLower();
        if (strReceivedName == strImRecipientName)
        {
          mapAvatarNameToKey[strImRecipientName] = p.Key;
          avatarNameSearchEvent.Set();
        }
      }
    }

    /// <summary>
    /// Find nearby objects (prims) and get their UUIDs.  This is useful for locating
    /// animation objects such as pose balls.
    /// </summary>
    public List<Primitive>
    FindNearbyObjects(
      List<string> argumentList)
    {
      if ((argumentList.Count < 1) || (argumentList.Count > 2))
      {
        Tools.Log("Usage: near [radius] {search-string}\n");
        return null;
      }

      float searchRadius;
      if (!float.TryParse(argumentList[0], out searchRadius))
      {
        Tools.LogWarn("Invalid search radius: " + argumentList[0] + "\n");
        return null;
      }

      string searchString = (argumentList.Count > 1) ? argumentList[1] : String.Empty;

      Vector3 location = gridClient.Self.SimPosition;

      // Find objects within search radius.
      List<Primitive> prims = gridClient.Network.CurrentSim.ObjectsPrimitives.FindAll(
        delegate(Primitive prim)
        {
          Vector3 pos = prim.Position;
          return ((prim.ParentID == 0) && (pos != Vector3.Zero) && (Vector3.Distance(pos, location) < searchRadius));
        }
      );

      // Obtain properties of located objects.
      bool bComplete = RequestObjectProperties(prims, 250);

      foreach (Primitive p in prims)
      {
        string strPrimName = p.Properties != null ? p.Properties.Name : null;
        if (String.IsNullOrEmpty(searchString) || ((strPrimName != null) && (strPrimName.Contains(searchString))))
          Tools.Log(String.Format("Object '{0}': {1}\n", strPrimName, p.ID.ToString()));
      }

      if (!bComplete)
      {
        if (primsAwaitingProperties.Keys.Count > 0)
        {
          Tools.LogWarn("Unable to retrieve full properties for:\n");
          foreach (UUID uuid in primsAwaitingProperties.Keys)
            Tools.Log(uuid + "\n");
        }
      }

      Tools.LogInfo("Done searching for nearby objects\n");

      return prims;
    }

    /// <summary>
    /// Send a request for object properties.
    /// </summary>
    /// <param name="objects"></param>
    /// <param name="msPerRequest"></param>
    /// <returns></returns>
    private bool
    RequestObjectProperties(
      List<Primitive> objects,
      int msPerRequest)
    {
      // Create an array of the local IDs of all the prims whose properties we request.
      uint[] localids = new uint[objects.Count];

      lock (primsAwaitingProperties)
      {
        primsAwaitingProperties.Clear();

        for (int i = 0; i < objects.Count; ++i)
        {
          localids[i] = objects[i].LocalID;
          primsAwaitingProperties.Add(objects[i].ID, objects[i]);
        }
      }

      // Send a request for the object properties.
      gridClient.Objects.SelectObjects(gridClient.Network.CurrentSim, localids);

      return propertiesReceivedEvent.WaitOne(2000 + msPerRequest*objects.Count, false);
    }

    /// <summary>
    /// Callback: Receive object properties.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    ObjectPropertiesCallback(
      object sender,
      ObjectPropertiesEventArgs e)
    {
      lock (primsAwaitingProperties)
      {
        Primitive prim;
        if (primsAwaitingProperties.TryGetValue(e.Properties.ObjectID, out prim))
          prim.Properties = e.Properties;

        primsAwaitingProperties.Remove(e.Properties.ObjectID);

        if (primsAwaitingProperties.Count == 0)
          propertiesReceivedEvent.Set();
      }
    }

    /// <summary>
    /// Sit on an object.  This also starts animations offered by the object (e.g.,
    /// dances from a dance ball).
    /// </summary>
    /// <param name="argumentList"></param>
    public void
    SitOnObject(
      List<string> argumentList)
    {
      if (argumentList.Count != 1)
      {
        Tools.Log("Usage: siton UUID\n");
        return;
      }

      UUID targetUuid;

      if (UUID.TryParse(argumentList[0], out targetUuid))
      {
        Primitive targetPrim = gridClient.Network.CurrentSim.ObjectsPrimitives.Find(
          delegate(Primitive prim)
          {
            return prim.ID == targetUuid;
          }
        );

        if (targetPrim != null)
        {
          gridClient.Self.RequestSit(targetPrim.ID, Vector3.Zero);
          gridClient.Self.Sit();
          Tools.LogInfo("Requested to sit on prim " + targetPrim.ID.ToString() +
              " (" + targetPrim.LocalID + ")\n");
          return;
        }
      }

      Tools.LogWarn("Couldn't find a prim to sit on with UUID " + argumentList[0] + "\n");
    }

    public void
    SitOnObject(
      UUID objectUuid)
    {
      SitOnObject(new List<string>() { objectUuid.ToString() });
    }

    /// <summary>
    /// Via heuristics, attempt to find and use a nearby pose-ball object.  These are
    /// typically rezzed near an avatar by clicking on a pose-ball provider (e.g, for
    /// couples dancing).
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    public bool
    UseNearbyPoseBall()
    {
      // Get list of nearby objects.
      var prims = FindNearbyObjects(new List<string>() { "10" });
      if (null == prims || 0 == prims.Count)
      {
        Tools.LogWarn("No nearby objects located\n");
        return false;
      }

      // Search for object names likely to be dance balls.
      foreach (Primitive p in prims)
      {
        if (null == p.Properties)
          continue;

        string strPrimName = p.Properties.Name.ToLower();

        // For now, just do a simple check that works in specific cases.
        // TODO: Make this more general.
        if (strPrimName.Contains("ball"))
        {
          Tools.LogInfo("Using candidate pose ball: " + strPrimName + "\n");
          SitOnObject(p.ID);
          return true;
        }
      }

      Tools.LogWarn("Unable to find a nearby pose ball\n");
      return false;
    }

    /// <summary>
    /// Pay L$ to an object.
    /// </summary>
    /// <param name="argumentList"></param>
    public void
    PayObject(
      List<string> argumentList)
    {
      if (argumentList.Count != 2)
      {
        Tools.Log("Usage: pay [UUID] [amount]\n");
        return;
      }

      UUID targetUuid;
      int paymentAmount;

      if (!UUID.TryParse(argumentList[0], out targetUuid))
      {
        Tools.LogWarn("Invalid object UUID: " + argumentList[0] + "\n");
        return;
      }

      if (!Int32.TryParse(argumentList[1], out paymentAmount))
      {
        Tools.LogWarn("Invalid paymeny amount: " + argumentList[1] + "\n");
        return;
      }

      gridClient.Self.GiveObjectMoney(targetUuid, paymentAmount, "");
      Tools.LogInfo("Paid object " + targetUuid.ToString() + " " + paymentAmount + "\n");
    }

    /// <summary>
    /// Callback: Execute period actions when timer has elapsed.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    PeriodicActionsTimerElapsedHandler(
      object sender,
      System.Timers.ElapsedEventArgs e)
    {
      if (bFollowActive)
        PeriodicFollowUpdate();
    }

    /// <summary>
    /// Parameters for following avatars
    /// </summary>
    const float DISTANCE_BUFFER = 3.0f;
    uint targetLocalID = 0;
    bool bFollowActive = false;
    string strFollowAvatarName = null;

    /// <summary>
    /// Start or stop following the specified target avatar. This code is adapted from the
    /// libomv test client.
    /// </summary>
    /// <param name="argumentList"></param>
    void
    FollowAvatar(
      List<string> argumentList)
    {
      if (argumentList.Count < 1)
      {
        Tools.Log("Usage: follow [First] [Last or Resident]\n");
        return;
      }

      string strTargetAvatarName = argumentList[0];
      if ("off" == strTargetAvatarName)
      {
        bFollowActive = false;
        targetLocalID = 0;
        gridClient.Self.AutoPilotCancel();
        Tools.LogInfo("Stopped following\n");
        return;
      }

      strTargetAvatarName += ((argumentList.Count > 1) ? " " + argumentList[1] : " Resident");

      strFollowAvatarName = strTargetAvatarName;
      lock (gridClient.Network.Simulators)
      {
        for (int i = 0; i < gridClient.Network.Simulators.Count; i++)
        {
          Avatar targetAvatar = gridClient.Network.Simulators[i].ObjectsAvatars.Find(
            delegate (Avatar avatar)
            {
              return avatar.Name == strTargetAvatarName;
            });

          if (targetAvatar != null)
          {
            targetLocalID = targetAvatar.LocalID;
            bFollowActive = true;
            Tools.LogInfo("Starting to follow " + strTargetAvatarName + "\n");
            return;
          }
        }
      }

      if (bFollowActive)
      {
        gridClient.Self.AutoPilotCancel();
        bFollowActive = false;
      }

      Tools.LogWarn("Unable to follow " + strTargetAvatarName + ". Client may not be able to see that avatar.\n");
    }

    /// <summary>
    /// This function should be called regularly (e.g., every 1/2 second) to implement
    /// following avatars.
    /// </summary>
	  void
    PeriodicFollowUpdate()
    {
      if (bFollowActive)
      {
        // Find the target position
        lock (gridClient.Network.Simulators)
        {
          for (int i = 0; i < gridClient.Network.Simulators.Count; i++)
          {
            Avatar targetAv;

            if (gridClient.Network.Simulators[i].ObjectsAvatars.TryGetValue(targetLocalID, out targetAv))
            {
              float distance = 0.0f;

              if (gridClient.Network.Simulators[i] == gridClient.Network.CurrentSim)
              {
                distance = Vector3.Distance(targetAv.Position, gridClient.Self.SimPosition);
              }
              else
              {
                // FIXME: Calculate global distances
              }

              if (distance > DISTANCE_BUFFER)
              {
                uint regionX, regionY;
                Utils.LongToUInts(gridClient.Network.Simulators[i].Handle, out regionX, out regionY);

                double xTarget = (double)targetAv.Position.X + (double)regionX;
                double yTarget = (double)targetAv.Position.Y + (double)regionY;
                double zTarget = targetAv.Position.Z - 2f;

                Logger.DebugLog(String.Format("[Autopilot] {0} meters away from the target, starting autopilot to <{1},{2},{3}>",
                  distance, xTarget, yTarget, zTarget), gridClient);
                Tools.LogInfo(String.Format("[Autopilot] {0} meters away from the target, starting autopilot to <{1},{2},{3}>",
                  distance, xTarget, yTarget, zTarget));

                gridClient.Self.AutoPilot(xTarget, yTarget, zTarget);
              }
              else
              {
                // We are in range of the target and moving, stop moving
                gridClient.Self.AutoPilotCancel();
              }
            }
          }
        }
      }
    }

    /// <summary>
    /// Callback: Handle autopilot alers for following avatars.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void
    AlertMessageHandler(
      object sender,
      PacketReceivedEventArgs e)
    {
      Packet packet = e.Packet;

      AlertMessagePacket alert = (AlertMessagePacket)packet;
      string message = Utils.BytesToString(alert.AlertData.Message);

      if (message.Contains("Autopilot cancel"))
      {
        Logger.Log("FollowCommand: " + message, Helpers.LogLevel.Info, gridClient);
      }
    }

    /// <summary>
    /// Execute a teleport.
    /// </summary>
    /// <param name="argumentList"></param>
    public void
    Teleport(
      List<string> argumentList)
    {
      if (argumentList.Count != 4)
      {
        Tools.Log("Usage: tp [sim_name] [x] [y] [z]\n");
        return;
      }

      int x, y, z;
      if (Int32.TryParse(argumentList[1], out x) && Int32.TryParse(argumentList[2], out y) && Int32.TryParse(argumentList[3], out z))
      {
        // Format: Elysian_Fields --> Elysian Fields
        string strSimName = argumentList[0].Replace('_', ' ');
        if (gridClient.Self.Teleport(strSimName, new Vector3(x, y, z)))
          Tools.LogInfo("Teleported to " + gridClient.Network.CurrentSim + " (" + strSimName + "/" + x + "/" + y + "/" + z + ")");
        else
          Tools.LogInfo("Error: Failed teleport to " + strSimName + "/" + x + "/" + y + "/" + z + "\n" +
            gridClient.Self.TeleportMessage);
      }
      else
        Tools.LogWarn("Invalid TP arguments\n");
    }

    /// <summary>
    /// Display available console commands.
    /// </summary>
    public void
    UsageInformation()
    {
      Tools.Log("\nCommands:\n");
      Tools.Log("  help                                 - print this message\n");
      Tools.Log("  near [radius] {search string}        - find nearby objects (listing UUIDs)\n");
      Tools.Log("  siton [UUID]                         - sit on object (approving animation)\n");
      Tools.Log("  pose                                 - try to find and use a nearby pose ball\n");
      Tools.Log("  stand                                - stand (canceling animation)\n");
      Tools.Log("  im [first] [last] [message]          - send a private IM to an avatar\n");
      Tools.Log("  pay [UUID]                           - give L$ to an object (for purchases)\n");
      Tools.Log("  follow [First|off] [Last|Resident]   - start or stop following an avatar\n");
      Tools.Log("  tp [sim x y z]                       - teleport to sim/x/y/z\n");
      Tools.Log("  quit                                 - logout and exit program\n");
      Tools.Log("\n");
    }

    /// <summary>
    /// Read and execute console commands.
    /// </summary>
    public void
    CommandConsole()
    {
      for (;;)
      {
        Tools.Log("Command> ");
        string strCommandString = Console.ReadLine();

        List<string> argumentList = strCommandString.Split(' ').ToList();
        string strCommand = argumentList[0];
        argumentList.RemoveAt(0);

        if ("help" == strCommand)
          UsageInformation();
        else if ("near" == strCommand)
          FindNearbyObjects(argumentList);
        else if ("siton" == strCommand)
          SitOnObject(argumentList);
        else if ("pose" == strCommand)
          UseNearbyPoseBall();
        else if ("stand" == strCommand)
          gridClient.Self.Stand();
        else if ("im" == strCommand)
          SendInstantMessage(argumentList);
        else if ("pay" == strCommand)
          PayObject(argumentList);
        else if ("follow" == strCommand)
          FollowAvatar(argumentList);
        else if ("tp" == strCommand)
          Teleport(argumentList);
        else if ("quit" == strCommand)
          break;
        else if ("" != strCommand)
          Tools.Log("Unrecognized command: " + strCommand + "\n");
      }
    }

  } // class AnimationBotManager


  /// <summary>
  /// Program entry point
  /// </summary>
  class MainProgram
  {
    /// <summary>
    /// This is the main application entry point.
    /// </summary>
    [STAThread]
    static void
    Main(
      string[] args)
    {
      Tools.Log("OpenMetaverse Animation Bot " + AnimationBotManager.Version + "\n");
      Tools.Log("Copyright (c) 2015 MHJ.  All rights reserved.\n\n");

      AnimationBotManager botManager = null;

      try
      {
        if (args.Length < 3)
        {
          Tools.Log(String.Format("Usage: {0} [first name] [last name] [password]\n", Tools.ExecutableName));
          return;
        }

        botManager = new AnimationBotManager(args[0], args[1], args[2]);

        // Log in.
        if (botManager.LogIn())
          Tools.LogInfo("Successfully logged in " + args[0] + " " + args[1]);
        else
          Tools.Quit(-1, "Unable to log in " + args[0] + " " + args[1]);

        Thread.Sleep(5000);

        // Read and execute user commands.
        botManager.CommandConsole();

        // Log out.
        Tools.LogInfo("Logging out " + args[0] + " " + args[1] + "...\n");
        botManager.LogOut();
      }
      catch (Exception e)
      {
        Tools.LogWarn("Exception caught: " + e.ToString() + "\n");
        if (botManager != null)
          botManager.LogOut();
      }
    } // Main()

  } // class MainProgram

} // namespace
