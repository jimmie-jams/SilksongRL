using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SilksongRL
{
    public enum MoveDirection { None = 0, Left = 1, Right = 2 }
    public enum LookDirection { None = 0, Up = 1, Down = 2 }

    /// <summary>
    /// Available action space types for different encounters.
    /// </summary>
    public enum ActionSpaceType
    {
        // Basic: move, look, jump, attack (4 actions)
        Basic,

        // Extended: move, look, jump, attack, dash (5 actions)
        Extended
    }

    public class Action
    {
        public MoveDirection move;
        public LookDirection look;
        public bool jump;
        public bool attack;
        // Extended action space fields
        public bool dash;


        public Action()
        {
            move = MoveDirection.None;
            look = LookDirection.None;
            jump = false;
            attack = false;
            dash = false;
        }
    }

    /// <summary>
    /// Manages action space definitions and conversions.
    /// Supports multiple hardcoded action space types for different encounters.
    /// </summary>
    public static class ActionManager
    {
        public static int[] GetActionSpaceShape(ActionSpaceType type)
        {
            switch (type)
            {
                case ActionSpaceType.Basic:
                    return new int[] { 3, 3, 2, 2 };        // move(3), look(3), jump(2), attack(2)
                case ActionSpaceType.Extended:
                    return new int[] { 3, 3, 2, 2, 2 };     // + dash(2)
                default:
                    throw new System.ArgumentException($"Unknown action space type: {type}");
            }
        }

        public static Action ArrayToAction(ActionResponse response, ActionSpaceType type)
        {
            if (response?.action == null)
            {
                RLManager.StaticLogger?.LogError("[ActionManager] Invalid action response received");
                return new Action();
            }

            int expectedSize = GetActionSpaceShape(type).Length;
            if (response.action.Length != expectedSize)
            {
                RLManager.StaticLogger?.LogError($"[ActionManager] Action array size mismatch. Expected {expectedSize}, got {response.action.Length}");
                return new Action();
            }

            var action = new Action
            {
                move = (MoveDirection)response.action[0],
                look = (LookDirection)response.action[1],
                jump = response.action[2] == 1,
                attack = response.action[3] == 1
            };

            if (type == ActionSpaceType.Extended)
            {
                action.dash = response.action[4] == 1;
            }

            return action;
        }


        public static int[] ActionToArray(Action action, ActionSpaceType type)
        {
            switch (type)
            {
                case ActionSpaceType.Basic:
                    return new int[]
                    {
                        (int)action.move,
                        (int)action.look,
                        action.jump ? 1 : 0,
                        action.attack ? 1 : 0
                    };
                case ActionSpaceType.Extended:
                    return new int[]
                    {
                        (int)action.move,
                        (int)action.look,
                        action.jump ? 1 : 0,
                        action.attack ? 1 : 0,
                        action.dash ? 1 : 0
                    };
                default:
                    throw new System.ArgumentException($"Unknown action space type: {type}");
            }
        }

        /// <summary>
        /// Gets the key state for a given key name based on the action and action space type.
        /// Returns null if the key is not handled.
        /// </summary>
        public static bool? GetKeyState(string keyName, Action action, ActionSpaceType type)
        {
            if (action == null)
                return null;

            // Basic keys (always available)
            switch (keyName)
            {
                case "leftArrow":
                    return action.move == MoveDirection.Left;
                case "rightArrow":
                    return action.move == MoveDirection.Right;
                case "upArrow":
                    return action.look == LookDirection.Up;
                case "downArrow":
                    return action.look == LookDirection.Down;
                case "z":
                    return action.jump;
                case "x":
                    return action.attack;
            }

            // Extended keys
            if (type == ActionSpaceType.Extended)
            {
                switch (keyName)
                {
                    case "c":
                        return action.dash;
                }
            }

            return null;
        }
    }


    // Harmony patch for the new Unity Input System
    // Patches ButtonControl.isPressed to intercept key presses
    [HarmonyPatch(typeof(ButtonControl), "get_isPressed")]
    public static class ButtonControlPatch
    {
        public static bool Prefix(ButtonControl __instance, ref bool __result)
        {
            if (!RLManager.isAgentControlEnabled)
                return true;

            var action = RLManager.currentAction;
            if (action == null)
                return true;

            string keyName = __instance.name;

            bool? keyState = ActionManager.GetKeyState(keyName, action, RLManager.CurrentActionSpaceType);
            if (keyState.HasValue)
            {
                __result = keyState.Value;
                return false;
            }

            return true;
        }
    }

}
