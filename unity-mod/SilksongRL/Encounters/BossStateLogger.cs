using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Logs every state change in the boss's PlayMaker FSMs and every sprite animation it plays,
    /// with how long the previous state lasted. For working out a boss's attacks, the way
    /// LaceEncounter.MapBossState maps Lace 1's. Turned on with LogBossStates in the config.
    /// Lines start with the wall clock, to line them up with anything logged outside the game.
    /// </summary>
    public class BossStateLogger : MonoBehaviour
    {
        private HealthManager boss;
        private PlayMakerFSM[] fsms;
        private tk2dSpriteAnimator animator;
        private string clip;
        private readonly Dictionary<PlayMakerFSM, (string state, float since)> states =
            new Dictionary<PlayMakerFSM, (string state, float since)>();

        private void Update()
        {
            HealthManager current = RLManager.Boss;
            if (current == null) return;

            // Every reset loads a new copy of the boss
            if (current != boss)
            {
                boss = current;
                fsms = boss.GetComponents<PlayMakerFSM>();
                animator = boss.GetComponent<tk2dSpriteAnimator>();
                clip = null;
                states.Clear();
                Log($"--- {boss.name}, FSMs: {string.Join(", ", fsms.Select(f => f.FsmName))}");
            }

            float now = Time.time;
            foreach (PlayMakerFSM fsm in fsms)
            {
                if (fsm == null) continue;
                string state = fsm.ActiveStateName;
                states.TryGetValue(fsm, out var previous);
                if (previous.state == state) continue;

                string lasted = previous.state != null ? $"  (after {now - previous.since:F2}s in {previous.state})" : "";
                Log($"t={now:F2} hp={boss.hp} {fsm.FsmName}: {state}{lasted}");
                states[fsm] = (state, now);
            }

            string playing = animator != null && animator.CurrentClip != null ? animator.CurrentClip.name : null;
            if (playing != clip)
            {
                clip = playing;
                Log($"t={now:F2} hp={boss.hp} clip: {clip ?? "-"}");
            }
        }

        private static void Log(string message)
        {
            RLManager.StaticLogger?.LogInfo($"[BossStates] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }
}
