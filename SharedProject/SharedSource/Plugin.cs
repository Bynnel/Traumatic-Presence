using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Text;
using Barotrauma;
using HarmonyLib;

namespace TraumaticPresence
{
    public partial class Plugin : IAssemblyPlugin
    {
        readonly Harmony harmony = new Harmony("lenny.barotrauma.discordrpc");
        public void Initialize()
        {
            // When your plugin is loading, use this instead of the constructor
            // Put any code here that does not rely on other plugins.
            InitClient();
            //RpcClient.Initialize();
        }

        public void OnLoadCompleted()
        {
            // After all plugins have loaded
            // Put code that interacts with other plugins here.
        }

        public void PreInitPatching()
        {
            // Not yet supported: Called during the Barotrauma startup phase before vanilla content is loaded.
        }

        public void Dispose()
        {
            // Cleanup your plugin!
            DebugConsole.NewMessage("Dispose() has been called.");
            RpcClient.Dispose();
            Timer.Dispose();
            //throw new NotImplementedException();
        }
    }
}
