/* // Requires: GUIAnnouncements */

using System;
using System.IO;    /* Needed for Path */
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;

// Quaternion.LookRotation((lastDeathPosition - player.eyes.position).normalized).eulerAngles.y

namespace Oxide.Plugins
{
    [Info("GoldenAKChallenge", "Lamego", "0.0.1")]

    class GoldenAKChallenge : RustPlugin
    {

        [PluginReference]
        private Plugin GUIAnnouncements;

		bool initialized = false;

        const int AK_ITEM_ID = -1461508848;
        const ulong GOLDEN_AK_SKIN_ID = 1167207039;

        static GoldenAKChallenge instance;

        #region AK tracking
        private BasePlayer holdingPlayer = null;        
        private DroppedItem dropAK = null;
        private Item holdAK = null;
        private StorageContainer containerAK = null;
        private ulong currentOwnerID = 0;   /* UID of the last user holding the AK */
        #endregion

        #region Map marker update
        private Timer _timer;
        private MapMarkerGenericRadius mapMarker = null;
        #endregion

        #region Game Data
        class StoredData
        {
            public ulong currentOwnerID = 0;
            public uint  AK_ID = 0;
            public StoredData()
            {
            }
        }        
        StoredData gameData;
        #endregion

        Vector3 lastMarkerPos = Vector3.zero;            

        StorageContainer CreateLargeBox(Vector3 position)
        {
            const string boxShortname = "box.wooden.large";
            const string boxPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";        
            ItemDefinition boxDef = ItemManager.FindItemDefinition(boxShortname);                
            StorageContainer box = GameManager.server.CreateEntity(boxPrefab, position, new Quaternion(), true) as StorageContainer;            
            box.Spawn();
            return box;
        }

        public Vector3 GetEventPosition()
        {
            int maxRetries = 100;
            Vector3 localeventPos;

            int blockedMask = LayerMask.GetMask(new[] { "Player (Server)", "Trigger", "Prevent Building" });
            List<int> BlockedLayers = new List<int> { (int)Layer.Water, (int)Layer.Construction, (int)Layer.Trigger, (int)Layer.Prevent_Building, (int)Layer.Deployed, (int)Layer.Tree };

            List<Vector3> monuments = new List<Vector3>(); // positions of monuments on the server
            monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>().Select(monument => monument.transform.position).ToList();

            do
            {                
                localeventPos = GetSafeDropPosition(RandomDropPosition());

                if (Interface.CallHook("OnGAKOpen",localeventPos) != null)
                {
                    localeventPos = Vector3.zero;
                    continue;
                }

                foreach (var monument in monuments)
                {
                    if (Vector3.Distance(localeventPos, monument) < 150f) // don't put the treasure chest near a monument
                    {
                        localeventPos = Vector3.zero;
                        break;
                    }
                }
            } while (localeventPos == Vector3.zero && --maxRetries > 0);

            return localeventPos;
        }

        public Vector3 GetSafeDropPosition(Vector3 position)
        {
            var eventRadius = 25f; /* */
            RaycastHit hit;
            position.y += 200f;
            int blockedMask = LayerMask.GetMask(new[] { "Player (Server)", "Trigger", "Prevent Building" });
            List<int> BlockedLayers = new List<int> { (int)Layer.Water, (int)Layer.Construction, (int)Layer.Trigger, (int)Layer.Prevent_Building, (int)Layer.Deployed, (int)Layer.Tree };

            if (Physics.Raycast(position, Vector3.down, out hit))
            {
                if (!BlockedLayers.Contains(hit.collider?.gameObject?.layer ?? BlockedLayers[0]))
                {
                    position.y = Mathf.Max(hit.point.y, TerrainMeta.HeightMap.GetHeight(position));

                    var colliders = Pool.GetList<Collider>();
                    Vis.Colliders(position, eventRadius, colliders, blockedMask, QueryTriggerInteraction.Collide);

                    bool blocked = colliders.Count > 0;

                    Pool.FreeList<Collider>(ref colliders);

                    if (!blocked)
                        return position;
                }
            }

            return Vector3.zero;
        }

        public Vector3 RandomDropPosition() // CargoPlane.RandomDropPosition()
        {
            var vector = Vector3.zero;
            SpawnFilter filter = new SpawnFilter();

            float num = 100f, x = TerrainMeta.Size.x / 3f;
            do
            {
                vector = Vector3Ex.Range(-x, x);
            }
            while (filter.GetFactor(vector) == 0f && (num -= 1f) > 0f);
            vector.y = 0f;
            return vector;
        }


        /// <summary>
        /// Finds player by SteamID
        /// </summary>
        /// <param name="steamid">SteamID of the player to be searched</param>
        /// <returns></returns>
        BasePlayer FindPlayerByID(ulong steamid)
        {
            BasePlayer targetplayer = BasePlayer.FindByID(steamid);
            if (targetplayer != null)
            {
                return targetplayer;
            }
            targetplayer = BasePlayer.FindSleeping(steamid);
            if (targetplayer != null)
            {
                return targetplayer;
            }
            return null;
        }


        public Vector3 GAKChestEvent(BasePlayer player = null) {
            Vector3 eventPos;
            var randomPos = GetEventPosition();                        
            if (randomPos == Vector3.zero)
            {
                return Vector3.zero;
            }

            eventPos = randomPos;
            Puts("Spawned Golden AK box at "+eventPos.ToString());

            StorageContainer container = CreateLargeBox(eventPos);
            if (!container)
            {
                PrintError("Unable to spaw container");
                return Vector3.zero;
            }
            UpdateMarker(eventPos);

            holdAK = ItemManager.CreateByItemID(AK_ITEM_ID, 1);     
            var weapon = holdAK.GetHeldEntity() as BaseProjectile;                   
            ulong skin_id = 0;
            skin_id = GOLDEN_AK_SKIN_ID;
            holdAK.skin = skin_id;                
            if (holdAK.GetHeldEntity() != null) 
            { 
                holdAK.GetHeldEntity().skinID  = skin_id;
            }
            container.inventory.Clear();
            weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity;
            holdAK.dirty = true;
            weapon.SendNetworkUpdateImmediate();            
            holdAK.MoveToContainer(container.inventory, -1, true);                        
            container.inventory.MarkDirty();      
            containerAK = container;
            return container.transform.position;
        }

        void MapMarkerRefresh()
        {       
            Vector3 trackedPos = Vector3.zero;
            if(holdingPlayer != null)
                trackedPos = holdingPlayer.transform.position;
            else if(dropAK != null)
                trackedPos = dropAK.transform.position;
            else if(containerAK != null)
            {
                trackedPos = containerAK.transform.position;
            }
            if(lastMarkerPos != trackedPos)
            {
                UpdateMarker(trackedPos);
                lastMarkerPos = trackedPos;
            }
        }      


		void Init()
        {   
            LoadData();			
        }

		void OnServerSave()
		{
			SaveData();
		}

        object OnItemPickup(Item item, BasePlayer player)        
        {

            if(item != dropAK.GetItem())   /* We only care about our ak */
                return null;

            holdingPlayer = player;
            holdAK = item;
            dropAK = null;            
            containerAK = null;

            if(player.userID != currentOwnerID)                      
            {
                currentOwnerID = player.userID;    
                string msg = String.Format("The Golden AK was picked by {0}", player.displayName);
                GUIAnnouncements?.Call("CreateAnnouncement", msg, "gray", "white", player);
            }
            
            
            return null;
        }

        void OnItemDropped(Item item, BaseEntity entity)
        {
            if(item != holdAK)   /* We only care about our ak */
                return;
            holdingPlayer = null;
            holdAK = null;
            dropAK = FindOnDroppedItems(item.uid);

        }
        

        void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            foreach (Item i in player.inventory.FindItemIDs(AK_ITEM_ID))
            {
                if(i == holdAK)
                {                    
                    Puts("Got my AK");
                    containerAK = null;
                    dropAK = null;
                    holdingPlayer = player;
                    if(player.userID != currentOwnerID) { /* Owner change */
                        currentOwnerID = player.userID;                    
                        string msg = String.Format("The Golden AK was picked by {0}", player.displayName);
                        GUIAnnouncements?.Call("CreateAnnouncement", msg, "gray", "white", player);
                    }
                    return;                    
                }
            }    

            // Holding player no longer holds it
            if (player == holdingPlayer)
            {
                if(entity is StorageContainer)
                {
                    containerAK = entity as StorageContainer;
                    holdingPlayer = null;
                }
            }

        }

        // Don't allow AK to be placed on destrutible containers
        object CanAcceptItem(ItemContainer container, Item item)
        {
            if(item == holdAK)
            {
                var entityOwner = container.entityOwner;
                if(entityOwner is Recycler || entityOwner is ResearchTable || entityOwner is LootContainer )
                    return ItemContainer.CanAcceptResult.CannotAccept;
            }
            return null;
        }

        DroppedItem FindOnDroppedItems(uint uid)
        {
            var dropped_items = UnityEngine.Object.FindObjectsOfType<DroppedItem>();
            foreach(DroppedItem item in dropped_items)
            {
                if(item.GetItem().uid == uid)
                {
                    return item;
                }
            }
            return null;
        }

        void OnServerInitialized()
        {

            

			if (initialized)
				return;

            // Seen this code on other plugin but I am not sure if it's needed
            // The plugin is waiting for the itemList to be populated
			var itemList = ItemManager.itemList;
			if (itemList == null || itemList.Count == 0)
			{                
				NextTick(OnServerInitialized);
				return;
			}

            instance = this;

            UpdateTrackingObjects();


            //DownloadImages();

            if(holdAK == null && dropAK == null)
            {
                GAKChestEvent();
            } else
                Puts("Restored game from data");
            
            _timer = timer.Every(1, MapMarkerRefresh);            
            
        }

        private void UpdateTrackingObjects()
        {
            currentOwnerID = gameData.currentOwnerID;
            if(gameData.AK_ID == 0)
                return;

            // Search the AK on players inventory
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                foreach (Item i in player.inventory.FindItemIDs(AK_ITEM_ID))
                {
                    if(i.uid == gameData.AK_ID)
                    {
                        currentOwnerID = player.userID;
                        holdingPlayer = player;
                        holdAK = i;
                        dropAK = null;
                        containerAK = null;
                        return;
                    }
                }
            }

            // Search the AK on players inventory
            foreach(BasePlayer player in BasePlayer.sleepingPlayerList)
            {
                foreach (Item i in player.inventory.FindItemIDs(AK_ITEM_ID))
                {
                    if(i.uid == gameData.AK_ID)
                    {
                        currentOwnerID = player.userID;
                        holdingPlayer = player;
                        holdAK = i;
                        dropAK = null;
                        containerAK = null;
                        return;
                    }
                }
            }

            // Search the AK on dropped items
            dropAK = FindOnDroppedItems(gameData.AK_ID);
            if(dropAK != null)
            {
                holdAK = null;                
                holdingPlayer = null;
                containerAK = null;
                return;
            }

            // Search the AK on containers
            foreach(StorageContainer container in UnityEngine.Object.FindObjectsOfType<StorageContainer>())
            {
                foreach (Item i in container.inventory.FindItemsByItemID(AK_ITEM_ID))
                {
                    if(i.uid == gameData.AK_ID)
                    {
                        Puts("Found on container");
                        currentOwnerID = gameData.currentOwnerID;
                        holdingPlayer = null;
                        holdAK = i;
                        dropAK = null;
                        containerAK = container;
                    }
                }
            }
        }

        private void LoadData()
        {

            try
            {
                gameData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("GoldenAKChallenge");
            }
            catch (Exception ex)
            {
                if(ex is MissingMethodException)
                {
                    instance.Puts("No data was found.");
                    gameData = new StoredData();
                    return;
                }
                RaiseError($"Failed to load data file. ({ex.Message})\n");
            }
        }

        private void SaveData()
        {
            gameData.currentOwnerID = currentOwnerID;            
            gameData.AK_ID = (holdAK != null) ? holdAK.uid : dropAK.GetItem().uid;
            Interface.Oxide.DataFileSystem.WriteObject("GoldenAKChallenge", gameData);
        }

        private void UpdateMarker(Vector3 position)
        {
            mapMarker?.Kill();
            mapMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", position) as MapMarkerGenericRadius;
            mapMarker.alpha = 0.8f;
            mapMarker.color1 = Color.red;
            mapMarker.color2 = Color.red;
            mapMarker.radius = 2;
            mapMarker.Spawn();
            mapMarker.SendUpdate();
        }

        void Unload()
        {
            SaveData();
            mapMarker?.Kill();
        }           
    }
    
}