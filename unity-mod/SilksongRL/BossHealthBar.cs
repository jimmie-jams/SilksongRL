using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// A boss health bar along the bottom of the screen: the boss's name over a red bar. Recent hits
    /// show as a pale chunk that drains away once they stop, with the damage they did on the right.
    /// Drawn in OnGUI, after the screen capture is taken, so the agent never sees it.
    /// </summary>
    public class BossHealthBar : MonoBehaviour
    {
        // Sizes and positions as fractions of the screen height
        private const float Width = 1.2f;
        private const float Height = 0.015f;
        private const float Bottom = 0.1f; // gap under the bar
        private const float FontSize = 0.026f;

        private const float TrailHold = 0.8f;  // seconds after the last hit before the chunk drains
        private const float TrailDrain = 0.5f; // of the max HP per second

        private static readonly Color FrameColor = new Color32(112, 98, 74, 255);
        private static readonly Color BackColor = new Color32(14, 11, 11, 255);
        private static readonly Color FillColor = new Color32(150, 22, 18, 255);
        private static readonly Color TrailColor = new Color32(212, 168, 74, 255);
        private static readonly Color TextColor = new Color32(232, 226, 212, 255);

        private string title;
        private HealthManager boss;
        private int hp, damage;
        private float trailHp, lastHitTime;
        private Texture2D shade;
        private GUIStyle nameStyle, damageStyle;

        public void Initialize(string targetBoss)
        {
            // Lace_1 -> Lace, Savage_Beastfly -> Savage Beastfly
            title = Regex.Replace(targetBoss, @"_\d+$", "").Replace('_', ' ');
        }

        private void Update()
        {
            HealthManager current = RLManager.Boss;
            if (current == null)
            {
                boss = null;
                return;
            }

            int now = Mathf.Max(0, current.hp);
            if (current != boss || now > hp)
            {
                // A new boss, or this one healed: start over
                boss = current;
                hp = now;
                trailHp = now;
                damage = 0;
                return;
            }

            if (now < hp)
            {
                damage += hp - now;
                hp = now;
                lastHitTime = Time.unscaledTime;
            }

            // Real time, so it drains at the same pace at any game speed
            if (trailHp > hp && Time.unscaledTime - lastHitTime > TrailHold)
            {
                trailHp = Mathf.Max(hp, trailHp - TrailDrain * RLManager.BossMaxHP * Time.unscaledDeltaTime);
                if (trailHp <= hp)
                    damage = 0;
            }
        }

        private void OnGUI()
        {
            float maxHp = RLManager.BossMaxHP;
            if (boss == null || !boss.gameObject.activeInHierarchy || maxHp <= 0f || trailHp <= 0f
                || Event.current.type != EventType.Repaint)
                return;

            if (shade == null)
                Setup();

            float screenH = Screen.height;
            float w = Mathf.Round(screenH * Width);
            float h = Mathf.Round(screenH * Height);
            float border = Mathf.Max(1f, Mathf.Round(screenH * 0.0018f));
            var bar = new Rect(Mathf.Round((Screen.width - w) / 2), Mathf.Round(screenH * (1f - Bottom) - h), w, h);

            Color previous = GUI.color;
            Draw(new Rect(bar.x - border, bar.y - border, bar.width + 2 * border, bar.height + 2 * border),
                FrameColor, Texture2D.whiteTexture);
            Draw(bar, BackColor, Texture2D.whiteTexture);
            Draw(Part(bar, trailHp / maxHp), TrailColor, shade);
            Draw(Part(bar, hp / maxHp), FillColor, shade);

            nameStyle.fontSize = damageStyle.fontSize = Mathf.RoundToInt(screenH * FontSize);
            float textH = nameStyle.fontSize * 1.5f;
            var text = new Rect(bar.x + border, bar.y - border - textH, bar.width - 2 * border, textH);
            Label(text, title, nameStyle);
            if (damage > 0)
                Label(text, damage.ToString(), damageStyle);
            GUI.color = previous;
        }

        private void Setup()
        {
            // Lighter at the top, so the bar is not flat
            shade = new Texture2D(1, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 8; y++)
            {
                float v = Mathf.Lerp(0.65f, 1f, y / 7f);
                shade.SetPixel(0, y, new Color(v, v, v, 1f));
            }
            shade.Apply();

            // The game's title font, once its font bundle is loaded; otherwise Unity's default
            Font font = Resources.FindObjectsOfTypeAll<Font>()
                .FirstOrDefault(f => f.name.IndexOf("Trajan", StringComparison.OrdinalIgnoreCase) >= 0);

            nameStyle = new GUIStyle(GUI.skin.label) { font = font, alignment = TextAnchor.LowerLeft };
            nameStyle.normal.textColor = Color.white;
            damageStyle = new GUIStyle(nameStyle) { alignment = TextAnchor.LowerRight };
        }

        private static Rect Part(Rect bar, float fraction)
        {
            return new Rect(bar.x, bar.y, Mathf.Round(bar.width * Mathf.Clamp01(fraction)), bar.height);
        }

        private static void Draw(Rect rect, Color color, Texture texture)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
        }

        private static void Label(Rect rect, string text, GUIStyle style)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
            GUI.color = TextColor;
            GUI.Label(rect, text, style);
        }

        private void OnDestroy()
        {
            if (shade != null)
                Destroy(shade);
        }
    }
}
