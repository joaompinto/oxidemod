using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;

using Rust;
using UnityEngine;
using Convert = System.Convert;
using Oxide.Game.Rust;


namespace Oxide.Plugins
{
    [Info("WelcomeBot", "Lamego", "0.0.1", ResourceId = 856)]

    public class WelcomeBot : RustPlugin
    {
        //////////////////////////////////////////////////////
        ///  Fields
        //////////////////////////////////////////////////////
        private static Vector3 Vector3Down;
        private Vector3 eyesPosition;

        [PluginReference]
        Plugin HumanNPC;


        public class WaypointInfo
        {
            public float Speed;
            public Vector3 Position;            


            public WaypointInfo(Vector3 position, float speed)
            {
                Speed = speed;
                Position = position;
            }
        }

        bool TryGetPlayerView(BasePlayer player, out Quaternion viewAngle)
        {
            viewAngle = new Quaternion(0f, 0f, 0f, 0f);
            if (player.serverInput?.current == null) return false;
            viewAngle = Quaternion.Euler(player.serverInput.current.aimAngles);
        return true;
        }

        //////////////////////////////////////////////////////
        ///  OnServerInitialized()
        ///  called when the server is done being initialized
        //////////////////////////////////////////////////////
        void OnServerInitialized()
        {
            eyesPosition = new Vector3(1f, 1f, 0f);
            Vector3Down = new Vector3(0f, -1f, 0f);
        }

        void OnPlayerRespawn( BasePlayer player )
        {            
            Quaternion currentRot;
            Puts("On respawn");

            if (!TryGetPlayerView(player, out currentRot))
            {
                Puts($"Did not get player view"+  player.transform.position);
            }                
            Puts($"Player pos"+  player.transform.position);
            var npc = HumanNPC?.Call("CreateNPC", player.transform.position + eyesPosition, currentRot);
        }        
            
    }

}