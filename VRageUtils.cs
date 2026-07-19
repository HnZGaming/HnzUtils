using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace HnzUtils
{
    public static class VRageUtils
    {
        public static NetworkType NetworkType
        {
            get
            {
                if (MyAPIGateway.Utilities.IsDedicated) return NetworkType.DediServer;
                return MyAPIGateway.Session.IsServer ? NetworkType.SinglePlayer : NetworkType.DediClient;
            }
        }

        public static bool NetworkTypeIn(NetworkType networkTypes)
        {
            return (networkTypes & NetworkType) == NetworkType;
        }

        public static void AssertNetworkType(NetworkType networkTypes, string message = "invalid network type")
        {
            if (!NetworkTypeIn(networkTypes))
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        ///     Stable network channel id (FNV-1a 32-bit, xor-folded to 16). string.GetHashCode()
        ///     can't be used: .NET 10 reseeds it per process, so client and server never agree.
        /// </summary>
        public static ushort StableKey(string text)
        {
            var h = 2166136261;
            foreach (var c in text)
            {
                h = (h ^ c) * 16777619;
            }

            return (ushort)(h ^ (h >> 16));
        }

        public static bool IsNpc(long identityId)
        {
            // faction type, not steam id: a client can't resolve an offline player's steam id
            // and would call them an npc
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
            if (faction != null) return faction.FactionType != MyFactionTypes.PlayerMade;

            return false; // factionless & unknown: assume player
        }

        public static GridOwnerType GetOwnerType(long ownerId)
        {
            if (ownerId == 0) return GridOwnerType.Nobody;
            return IsNpc(ownerId) ? GridOwnerType.NPC : GridOwnerType.Player;
        }

        public static void UpdateStorageValue(this IMyEntity entity, Guid key, string value)
        {
            if (entity.Storage == null)
            {
                entity.Storage = new MyModStorageComponent();
            }

            entity.Storage.SetValue(key, value);
        }

        public static bool TryGetStorageValue(this IMyEntity entity, Guid key, out string value)
        {
            if (entity.Storage == null)
            {
                value = null;
                return false;
            }

            return entity.Storage.TryGetValue(key, out value);
        }

        public static MyDefinitionId ToDefinitionId(this SerializableDefinitionId id)
        {
            return new MyDefinitionId(id.TypeId, id.SubtypeName);
        }

        public static Vector3 CalculateNaturalGravity(Vector3 point)
        {
            float _;
            return MyAPIGateway.Physics.CalculateNaturalGravityAt(point, out _);
        }

        public static bool TryGetCharacter(ulong steamId, out IMyCharacter character)
        {
            var playerId = MyAPIGateway.Players.TryGetIdentityId(steamId);
            character = MyAPIGateway.Players.TryGetIdentityId(playerId)?.Character;
            return character != null;
        }

        public static IMyCharacter TryGetCharacter(long playerId)
        {
            return MyAPIGateway.Players.TryGetIdentityId(playerId)?.Character;
        }

        public static bool TryGetFaction(long blockId, out IMyFaction faction)
        {
            faction = null;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(blockId, out entity)) return false;

            var block = entity as IMyCubeBlock;
            if (block == null) return false;

            var ownerId = block.OwnerId;
            faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
            return faction != null;
        }

        public static void ClearItems(this IMyStoreBlock storeBlock)
        {
            // clear existing items
            var items = new List<IMyStoreItem>();
            storeBlock.GetStoreItems(items);
            foreach (var item in items)
            {
                storeBlock.RemoveStoreItem(item);
            }
        }

        public static bool TryGetEntityById<T>(long entityId, out T entity) where T : class, IMyEntity
        {
            entity = MyAPIGateway.Entities.GetEntityById(entityId) as T;
            return entity != null;
        }

        public static bool IsContractBlock(this IMyCubeBlock block)
        {
            return block.BlockDefinition.SubtypeId?.IndexOf("ContractBlock", StringComparison.Ordinal) > -1;
        }

        public static string FormatGps(string name, Vector3D position, string colorCode)
        {
            // example -- GPS:1-1-2:-5000000:-5000000:0:#FF75C9F1:
            return $"GPS:{name}:{position.X}:{position.Y}:{position.Z}:#{colorCode}";
        }

        public static bool TryGetVoxelIntersection(LineD line, IEnumerable<MyVoxelBase> voxels, out Vector3D intersection)
        {
            foreach (var v in voxels)
            {
                Vector3D? i;
                if (!v.GetIntersectionWithLine(ref line, out i) || i == null) continue;

                intersection = i.Value;
                return true;
            }

            intersection = Vector3D.Zero;
            return false;
        }

        public static bool TryGetFirstRaycastHitInfoByType<T>(Vector3 from, Vector3 to, out IHitInfo hitInfo) where T : class, IMyEntity
        {
            hitInfo = null;
            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastLongRay(from, to, out hit, false)) return false;

            var v = hit.HitEntity as T;
            if (v == null) return false;

            hitInfo = hit;
            return true;
        }

        public static bool IsInAnySafeZone(long entityId)
        {
            foreach (var zone in MySessionComponentSafeZones.SafeZones)
            {
                if (MySessionComponentSafeZones.IsInSafezone(entityId, zone))
                {
                    return true;
                }
            }

            return false;
        }

        public static void PlaySound(string cueName)
        {
            var character = MyAPIGateway.Session?.LocalHumanPlayer?.Character;
            if (character == null) return;

            var emitter = new MyEntity3DSoundEmitter(character as MyEntity);
            var sound = new MySoundPair(cueName);
            emitter.PlaySound(sound);
        }

        public static bool TryLoadStorageXmlFile<T>(string fileName, out T content)
        {
            MyLog.Default.Info($"[HnzUtils] storage file loading: {fileName}");

            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(fileName, typeof(T)))
            {
                MyLog.Default.Error($"[HnzUtils] storage file not found: {fileName}");
                content = default(T);
                return false;
            }

            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(fileName, typeof(T)))
                {
                    var contentText = reader.ReadToEnd();
                    content = MyAPIGateway.Utilities.SerializeFromXML<T>(contentText);
                    return true;
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Error($"[HnzUtils] storage file failed loading: {e}");
                content = default(T);
                return false;
            }
        }

        public static void SaveStorageFile<T>(string FileName, T content)
        {
            MyLog.Default.Info($"[HnzUtils] storage file saving: {FileName}");

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(T)))
                {
                    var xml = MyAPIGateway.Utilities.SerializeToXML(content);
                    writer.Write(xml);
                    MyLog.Default.Info("[HnzUtils] storage file saved");
                }
            }
            catch (Exception e)
            {
                MyLog.Default.Error($"[HnzUtils] storage file failed to save: {e}");
            }
        }

        public static bool TryCreatePhysicalObjectBuilder(MyDefinitionId defId, out MyObjectBuilder_PhysicalObject builder)
        {
            builder = MyObjectBuilderSerializer.CreateNewObject(defId) as MyObjectBuilder_PhysicalObject;
            return builder != null;
        }

        public static bool TryCalculateItemMinimalPrice(MyDefinitionId itemId, int scale, out int pricePerUnit)
        {
            pricePerUnit = 0;
            var p = 0;
            CalculateItemMinimalPrice(itemId, scale, ref p);
            if (p <= 0) return false;

            pricePerUnit = p;
            return true;
        }

        /// <summary>
        ///     Calculates the minimal price for a given item based on its blueprint prerequisites,
        ///     production speed, and efficiency multipliers.
        /// </summary>
        /// <param name="itemId">The item to price.</param>
        /// <param name="baseCostProductionSpeedMultiplier">Scaling factor for production time cost.</param>
        /// <param name="minimalPrice">Accumulator for the calculated price (passed by ref).</param>
        static void CalculateItemMinimalPrice(
            MyDefinitionId itemId,
            float baseCostProductionSpeedMultiplier,
            ref int minimalPrice)
        {
            // --- Step 1: Check if the item has a hardcoded minimal price ---
            MyPhysicalItemDefinition itemDef;
            var itemExists = MyDefinitionManager.Static.TryGetDefinition(itemId, out itemDef);
            if (itemExists && itemDef.MinimalPricePerUnit != -1)
            {
                // Use the predefined price directly — no need to calculate from blueprint
                minimalPrice += itemDef.MinimalPricePerUnit;
                return;
            }

            // --- Step 2: Look up the item's crafting blueprint ---
            MyBlueprintDefinitionBase blueprint = null;
            if (!MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(itemId, out blueprint))
                // No blueprint found — can't determine a price, bail out
                return;

            // --- Step 3: Determine the efficiency multiplier ---
            // Ingots (refined from ore) use a flat 1.0 efficiency.
            // Assembled components use the global assembler efficiency setting.
            var efficiencyMultiplier = itemDef.IsIngot
                ? 1f
                : MyAPIGateway.Session.AssemblerEfficiencyMultiplier;

            // --- Step 4: Calculate total ingredient cost ---
            var totalIngredientCost = 0;

            foreach (var ingredient in blueprint.Prerequisites)
            {
                // Recursively get the price of each ingredient
                var ingredientUnitPrice = 0;
                CalculateItemMinimalPrice(ingredient.Id, baseCostProductionSpeedMultiplier, ref ingredientUnitPrice);

                // Scale by how much of this ingredient is needed, accounting for efficiency loss
                var amountNeeded = (float)ingredient.Amount / efficiencyMultiplier;
                totalIngredientCost += (int)(ingredientUnitPrice * amountNeeded);
            }

            // --- Step 5: Determine the production speed multiplier ---
            // Ingots come from refineries; everything else from assemblers.
            var speedMultiplier = itemDef.IsIngot
                ? MyAPIGateway.Session.RefinerySpeedMultiplier
                : MyAPIGateway.Session.AssemblerSpeedMultiplier;

            // --- Step 6: Find this item in the blueprint's outputs and apply time cost ---
            foreach (var result in blueprint.Results)
            {
                if (result.Id != itemId) continue;

                var outputAmount = (float)result.Amount;
                if (outputAmount == 0f)
                {
                    // Sanity check — a result with zero output would cause a divide-by-zero
                    MyLog.Default.WriteToLogAndAssert("Amount is 0 for - " + result.Id);
                    return;
                }

                // Production time cost factor:
                //   A logarithmic penalty is added for items that take longer to produce.
                //   Faster machines (higher speedMultiplier) reduce this penalty.
                var productionTimeCostFactor =
                    1f + (float)Math.Log(blueprint.BaseProductionTimeInSeconds + 1f)
                    * baseCostProductionSpeedMultiplier
                    / speedMultiplier;

                // Final price = (total ingredient cost / output quantity) * time cost factor
                minimalPrice += (int)(totalIngredientCost * (1f / outputAmount) * productionTimeCostFactor);
                return;
            }
        }
    }
}