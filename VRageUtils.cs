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

        public static bool IsNpc(long identityId)
        {
            return MyAPIGateway.Players.TryGetSteamId(identityId) == 0;
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
            builder = null;

            var item = MyDefinitionManager.Static.GetDefinition(defId);
            if (item == null) return false;
            if (item.Id.TypeId.IsNull) return false;

            builder = MyObjectBuilderSerializer.CreateNewObject(defId) as MyObjectBuilder_PhysicalObject;
            return builder != null;
        }
    }
}