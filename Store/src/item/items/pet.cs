using System.ComponentModel;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Store.Extension;
using static Store.Store;
using static StoreApi.Store;

namespace Store;

[StoreItemType("pet")]
public class Item_pet : IItemModule
{
    public bool Equipable => true;
    public bool? RequiresAlive => null;

    private float _IfDistance = 50f;
    public class PetModel
    {
        public CDynamicProp? entity { get; set; }
        public Vector? targetPosition { get; set; }
        public Vector? currentPosition { get; set; }
        public Vector? LastPos { get; set; }
        public bool isMoving { get; set; } = false;
        public float moveSpeed { get; set; } = 180f;
        public int stationaryTicks { get; set; } = 0;
        public float rotationOffset { get; set; } = -90f;
        public float followDistance { get; set; } = 80f;
    }

    private static readonly Dictionary<CCSPlayerController, Dictionary<int, PetModel>> PlayerPetEntities = [];

    public void OnPluginStart()
    {
        if (Item.IsAnyItemExistInType("pet"))
        {
            Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            Instance.RegisterEventHandler<EventPlayerSpawned>(OnPlayerSpawned);
            Instance.RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            Instance.RegisterListener<Listeners.OnTick>(OnTick);
        }
    }

    private HookResult OnPlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
    {
        throw new NotImplementedException();
    }

    public void OnMapStart()
    {
        PlayerPetEntities.Clear();
    }

    public void OnServerPrecacheResources(ResourceManifest manifest)
    {
        var items = Item.GetItemsByType("pet");
        foreach (var item in items)
        {
            if (item.Value.TryGetValue("model", out var model) && !string.IsNullOrEmpty(model))
                manifest.AddResource(model);
        }
    }

    public bool OnEquip(CCSPlayerController player, Dictionary<string, string> item)
    {
        if (!item.TryGetValue("slot", out var slotStr) || !int.TryParse(slotStr, out var slot) || slot < 0)
            return false;
        var animation = item.TryGetValue("animation", out var anim) ? anim : string.Empty;
        var rotationOffsetStr = item.TryGetValue("rotation_offset", out var rotOffset) ? rotOffset : "-90";
        var speedStr = item.TryGetValue("move_speed", out var speed) ? speed : "180";
        float.TryParse(rotationOffsetStr, out var rotationOffset);
        float.TryParse(speedStr, out var moveSpeed);
        Equippet(player, item["model"], slot, animation, rotationOffset, moveSpeed);
        return true;
    }

    public bool OnUnequip(CCSPlayerController player, Dictionary<string, string> item, bool update)
    {
        if (!item.TryGetValue("slot", out var slotStr) || !int.TryParse(slotStr, out var slot))
            return false;
        UnEquippet(player, slot);
        return true;
    }

    public static void Equippet(CCSPlayerController player, string model, int slot, string animation = "", float rotationOffset = -90f, float moveSpeed = 180f)
    {
        UnEquippet(player, slot);
        Server.NextFrame(() =>
        {
            var pet = CreateSimplePet(player, model, animation, rotationOffset, moveSpeed);
            if (pet != null && pet.entity!.IsValid)
            {
                if (!PlayerPetEntities.ContainsKey(player))
                    PlayerPetEntities[player] = [];
                PlayerPetEntities[player][slot] = pet;
            }
        });
    }

    // PROSTY PET BEZ func_movelinear - działający
    public static PetModel? CreateSimplePet(CCSPlayerController player, string model, string animation = "", float rotationOffset = -90f, float moveSpeed = 180f)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return null;

        var origin = pawn.AbsOrigin;
        var offset = new Vector(-80, 0, 0);
        Vector startPos = origin! + offset;

        // Tworzymy tylko entity peta
        var entity = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (entity == null) return null;

        // WŁĄCZ KOLIZJĘ - ustaw odpowiednie flagi
        entity.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;
        entity.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;

        entity.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);
        entity.SetModel(model);
        entity.DispatchSpawn();

        if (!string.IsNullOrEmpty(animation))
        {
            entity.AcceptInput("SetAnimation", value: animation);
        }

        entity.Teleport(startPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));

        Server.PrintToChatAll($"[DEBUG] Pet stworzony na pozycji: {startPos.X:F1}, {startPos.Y:F1}, {startPos.Z:F1}");

        return new PetModel
        {
            entity = entity,
            currentPosition = startPos,
            targetPosition = startPos,
            LastPos = startPos,
            isMoving = false,
            rotationOffset = rotationOffset,
            followDistance = 80f,
            moveSpeed = moveSpeed
        };
    }

    public static void UnEquippet(CCSPlayerController player, int slot)
    {
        if (!PlayerPetEntities.TryGetValue(player, out var value) || !value.ContainsKey(slot))
            return;

        var pet = value[slot];
        if (pet.entity != null && pet.entity.IsValid)
            pet.entity.Remove();

        value.Remove(slot);
        if (value.Count == 0)
            PlayerPetEntities.Remove(player);
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;

        var gamerules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gamerules == null || gamerules.WarmupPeriod)
        {
            return HookResult.Continue;
        }

        var pets = Item.GetPlayerEquipments(player, "pet");
        foreach (var equip in pets)
        {
            var item = Item.GetItem(equip.UniqueId);
            if (item != null && item.TryGetValue("model", out var model))
            {
                var animation = item.TryGetValue("animation", out var anim) ? anim : string.Empty;
                var rotationOffsetStr = item.TryGetValue("rotation_offset", out var rotOffset) ? rotOffset : "-90";
                var speedStr = item.TryGetValue("move_speed", out var speed) ? speed : "180";
                float.TryParse(rotationOffsetStr, out var rotationOffset);
                float.TryParse(speedStr, out var moveSpeed);
                Equippet(player, model, equip.Slot, animation, rotationOffset, moveSpeed);
            }
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        if (PlayerPetEntities.ContainsKey(player))
        {
            foreach (var slot in PlayerPetEntities[player].Keys.ToList())
                UnEquippet(player, slot);
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        if (PlayerPetEntities.ContainsKey(player))
        {
            foreach (var slot in PlayerPetEntities[player].Keys.ToList())
                UnEquippet(player, slot);
        }
        return HookResult.Continue;
    }

    // WERSJA Z PŁYNNYM RUCHEM - bez func_movelinear
    // WERSJA Z INTELIGENTNYM PODĄŻANIEM - zatrzymuje się gdy jest blisko
    private void OnTick()
    {
        float deltaTime = Server.TickInterval;

        foreach (var kv in PlayerPetEntities.ToList())
        {
            var player = kv.Key;
            if (player == null || !player.IsValid) continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) continue;

            foreach (var petModel in kv.Value.Values)
            {
                if (petModel == null || petModel.entity == null || !petModel.entity.IsValid) continue;

                Vector playerPos = playerPawn.AbsOrigin!;
                Vector currentPos = petModel.currentPosition!;

                // Sprawdź odległość do gracza (nie pozycja za nim, ale rzeczywista odległość)
                float distanceToPlayer = (playerPos - currentPos).Length();

                // Pet rusza się tylko gdy jest daleko od gracza
                if (distanceToPlayer > petModel.followDistance) // 80 jednostek
                {
                    // Oblicz pozycję docelową (nie za graczem, ale w kierunku gracza)
                    Vector moveDirection = NormalizeVector(playerPos - currentPos);

                    // Oblicz nową pozycję z płynnym ruchem
                    float moveDistance = petModel.moveSpeed * deltaTime;
                    if (moveDistance > (distanceToPlayer - _IfDistance)) // Nie idź bliżej niż stopDistance
                        moveDistance = distanceToPlayer - _IfDistance;

                    Vector newPosition = currentPos + (moveDirection * moveDistance);

                    // KOLIZJA Z ZIEMIĄ - sprawdź wysokość terenu

                    // Oblicz rotację (patrzy na gracza)
                    Vector lookDirection = NormalizeVector(playerPos - newPosition);
                    float yaw = (float)(Math.Atan2(lookDirection.Y, lookDirection.X) * 180.0 / Math.PI) + petModel.rotationOffset;
                    QAngle newAngles = new QAngle(0, yaw, 0);

                    // Zastosuj ruch i rotację
                    petModel.entity.Teleport(newPosition, newAngles, new Vector(0, 0, 0));
                    petModel.currentPosition = newPosition;

                    // Animacje
                    if (!petModel.isMoving)
                    {
                        petModel.isMoving = true;
                        petModel.entity.AcceptInput("SetAnimation", value: "@courier_run");
                        Server.PrintToChatAll($"[PET] Rozpoczął ruch (odległość: {distanceToPlayer:F1})");
                    }
                    petModel.stationaryTicks = 0;
                }
                else if (distanceToPlayer <= 100) // 40 jednostek - ZATRZYMAJ SIĘ
                {
                    // Pet jest wystarczająco blisko - zatrzymaj się
                    petModel.stationaryTicks++;

                    if (petModel.isMoving && petModel.stationaryTicks > 50) // Szybciej zatrzymaj
                    {
                        petModel.isMoving = false;
                        petModel.entity.AcceptInput("SetAnimation", value: "@courier_idle");
                        Server.PrintToChatAll($"[PET] Zatrzymał się (jest wystarczająco blisko: {distanceToPlayer:F1})");

                        // Nadal patrzy na gracza
                        Vector lookDirection = NormalizeVector(playerPos - petModel.currentPosition!);
                        float yaw = (float)(Math.Atan2(lookDirection.Y, lookDirection.X) * 180.0 / Math.PI) + petModel.rotationOffset;
                        QAngle newAngles = new QAngle(0, yaw, 0);
                        petModel.entity.Teleport(petModel.entity.AbsOrigin, newAngles, new Vector(0, 0, 0));
                    }
                }
                // Jeśli distanceToPlayer jest między stopDistance a followDistance - nie rób nic (hysteresis)

                petModel.LastPos = petModel.currentPosition;
            }
        }
    }

    // Pomocnicza funkcja do obliczania kierunku gracza
    private Vector GetPlayerForwardVector(CCSPlayerPawn playerPawn)
    {
        QAngle eyeAngles = playerPawn.EyeAngles!;
        float yawRadians = eyeAngles.Y * (float)(Math.PI / 180.0);

        return new Vector(
            (float)Math.Cos(yawRadians),
            (float)Math.Sin(yawRadians),
            0
        );
    }

    // POPRAWIONA funkcja normalize - oblicza kierunek jednostkowy
    private Vector NormalizeVector(Vector vector)
    {
        float length = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
        if (length == 0) return new Vector(0, 0, 0);

        return new Vector(
            vector.X / length,
            vector.Y / length,
            vector.Z / length
        );
    }
}