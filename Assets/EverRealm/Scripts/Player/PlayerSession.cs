using System.Collections.Generic;
using EverRealm.Characters;
using EverRealm.Combat;
using EverRealm.Settlements;
using EverRealm.World;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EverRealm.Player
{
    /// <summary>
    /// The local viewer: which body the player controls, the camera and shake that follow it, and the
    /// death screen. Death is permanent; until succession exists, a debug key (N) spawns a new human in the
    /// same world and takes control of it. Bodies the player starts in are hamlet residents; while controlled
    /// their villager brain is off, and a living body the player lets go of carries on as a villager.
    /// </summary>
    public sealed class PlayerSession : MonoBehaviour
    {
        [SerializeField] WorldBuilder world;
        [SerializeField] HamletPopulation population;
        [SerializeField] PlayerController humanPrefab;
        [Tooltip("Body controlled when the scene starts.")]
        [SerializeField] PlayerController initialBody;
        [SerializeField] CinemachineCamera playerCamera;
        [SerializeField] Transform viewCamera;
        [SerializeField] CinemachineImpulseSource impulseSource;

        // Dev-only until a succession mechanic exists.
        readonly InputAction newCharacterAction = new InputAction("New Character", binding: "<Keyboard>/n");
        readonly List<PlayerController> spawnedHumans = new List<PlayerController>();
        GUIStyle deathStyle;

        public PlayerController Current { get; private set; }
        public Combatant CurrentBody => Current != null ? Current.Body : null;
        public bool IsDead => CurrentBody != null && CurrentBody.IsDead;

        void OnEnable()
        {
            world.Generated += OnWorldGenerated;
            newCharacterAction.performed += OnNewCharacter;
            newCharacterAction.Enable();
        }

        void OnDisable()
        {
            world.Generated -= OnWorldGenerated;
            newCharacterAction.performed -= OnNewCharacter;
            newCharacterAction.Disable();
        }

        // WorldBuilder generates in Awake, before this subscribes.
        void Start()
        {
            spawnedHumans.Add(initialBody);
            PlaceAtSpawn(initialBody);
            MoveIn(initialBody);
            Possess(initialBody);
        }

        public void Possess(PlayerController body)
        {
            if (Current != null) Release(Current);
            Current = body;
            if (body.TryGetComponent(out VillagerAI villager)) villager.enabled = false;
            body.Bind(viewCamera);
            body.enabled = !body.Body.IsDead;
            playerCamera.Target.TrackingTarget = body.CameraTarget;
            body.GetComponent<CombatFeedback>().ImpulseSource = impulseSource;
            body.Body.Died += OnCurrentDied;
        }

        void Release(PlayerController body)
        {
            body.enabled = false;
            body.Body.Died -= OnCurrentDied;
            body.GetComponent<CombatFeedback>().ImpulseSource = null;
            if (body.TryGetComponent(out VillagerAI villager) && villager.IsAssigned && !body.Body.IsDead) villager.enabled = true;
        }

        void MoveIn(PlayerController body)
        {
            if (population != null && body.TryGetComponent(out VillagerAI villager)) population.Adopt(villager);
        }

        void OnCurrentDied() => Current.enabled = false; // The brain lets go; the body stays where it fell.

        void OnNewCharacter(InputAction.CallbackContext _)
        {
            PlayerController body = Instantiate(humanPrefab, world.FindSpawnPoint(), Quaternion.identity);
            body.name = $"Human {spawnedHumans.Count + 1}";
            spawnedHumans.Add(body);
            PlaceAtSpawn(body);
            MoveIn(body);
            Possess(body);
        }

        /// <summary>Debug "new island": human corpses are cleared and the controlled body moves to the new spawn.</summary>
        void OnWorldGenerated()
        {
            spawnedHumans.RemoveAll(h => h == null);
            foreach (PlayerController human in spawnedHumans.ToArray())
            {
                if (human != Current && human.Body.IsDead)
                {
                    spawnedHumans.Remove(human);
                    Destroy(human.gameObject);
                }
            }
            if (Current != null) PlaceAtSpawn(Current);
        }

        void PlaceAtSpawn(PlayerController body)
        {
            body.GetComponent<CharacterMotor>().Teleport(world.FindSpawnPoint());
            if (body.TryGetComponent(out OceanRescue rescue)) rescue.ResetSafePosition();
        }

        void OnGUI()
        {
            if (!IsDead) return;
            deathStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 26, richText = true };
            var rect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.35f, 520f, 110f);
            GUI.Box(rect, "<b>You died.</b>\n<size=16><color=#bbbbbb>Debug: press N to take control of a new character</color></size>", deathStyle);
        }
    }
}
