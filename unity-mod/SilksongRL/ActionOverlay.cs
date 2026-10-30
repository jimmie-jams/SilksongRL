using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Shows the agent's actions as a network's output layer along the top of the screen, right of
    /// Hornet's health: a node per action, lit while it is chosen, with edges fanning in from below.
    /// Doing nothing (value 0 of a head) has no node, so it leaves that head dark. Drawn in OnGUI,
    /// after the screen capture is taken, so the agent never sees it.
    /// </summary>
    public class ActionOverlay : MonoBehaviour
    {
        // Icons (Icons/*.png) for values 1 and up of each head, in ActionToArray's order
        private static readonly string[][] IconNames =
        {
            new[] { "left", "right" },
            new[] { "up", "down" },
            new[] { "jump" },
            new[] { "attack", "bind" },
            new[] { "dash" },
        };

        // Sizes and positions as fractions of the screen height, which the HUD scales with
        private const float NodeSize = 0.068f;
        private const float Left = 0.74f; // first node's left edge, clear of 10 masks
        private const float Top = 0.09f;  // node centres

        // In node sizes
        private const float Pitch = 1.3f;
        private const float HeadGap = 0.6f;
        private const float EdgeLength = 1.5f;   // how far below the node the edges start
        private const float EdgeReach = 1.4f;    // horizontal spread of a node's edges
        private const float EdgeSpacing = 0.45f; // between edge starts, shared by every node

        private const float EdgeAlpha = 0.22f;
        private const float LitEdgeAlpha = 0.8f;
        private const float FadeSeconds = 0.15f;
        private static readonly Color FillDark = new Color32(22, 22, 26, 255);
        private static readonly Color IconDark = new Color32(12, 12, 14, 255);

        private Texture2D disk, ring, glow, edge;
        private Texture2D[][] icons;
        // Per head and node, where node n is value n + 1
        private readonly float[,] brightness = new float[5, 2];
        private readonly Vector2[,] centres = new Vector2[5, 2];

        private void Awake()
        {
            disk = Radial(r => (1f - r) * 64f);
            ring = Radial(r => Mathf.Min((1f - r) * 64f, (r - 0.88f) * 64f));
            glow = Radial(r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
            edge = EdgeTexture();
            icons = IconNames.Select(head => head.Select(LoadIcon).ToArray()).ToArray();
        }

        private void Update()
        {
            var action = RLManager.currentAction;
            if (!RLManager.isAgentControlEnabled || action == null)
                return;

            var type = RLManager.CurrentActionSpaceType;
            int[] chosen = ActionManager.ActionToArray(action, type);
            int[] shape = ActionManager.GetActionSpaceShape(type);
            float fade = Time.unscaledDeltaTime / FadeSeconds;
            for (int h = 0; h < shape.Length; h++)
                for (int n = 0; n < shape[h] - 1; n++)
                    brightness[h, n] = chosen[h] == n + 1 ? 1f : Mathf.Max(0f, brightness[h, n] - fade);
        }

        private void OnGUI()
        {
            if (!RLManager.isAgentControlEnabled || Event.current.type != EventType.Repaint)
                return;

            int[] shape = ActionManager.GetActionSpaceShape(RLManager.CurrentActionSpaceType);
            float d = Mathf.Round(Screen.height * NodeSize);
            float left = Screen.height * Left + d / 2;
            float right = left;
            float headLeft = left;
            for (int h = 0; h < shape.Length; h++)
            {
                for (int n = 0; n < shape[h] - 1; n++)
                {
                    centres[h, n] = new Vector2(Mathf.Round(headLeft + n * d * Pitch), Mathf.Round(Screen.height * Top));
                    right = Mathf.Max(right, centres[h, n].x);
                }
                headLeft += ((shape[h] - 1) * Pitch + HeadGap) * d;
            }

            Color previous = GUI.color;
            // Edges, then lit edges, then glows, then nodes, so nothing lit is drawn over
            for (int pass = 0; pass < 4; pass++)
                for (int h = 0; h < shape.Length; h++)
                    for (int n = 0; n < shape[h] - 1; n++)
                    {
                        float b = brightness[h, n];
                        var node = new Rect(centres[h, n].x - d / 2, centres[h, n].y - d / 2, d, d);
                        if (pass == 0)
                            DrawEdges(node, left, right, EdgeAlpha);
                        else if (pass == 1 && b > 0f)
                            DrawEdges(node, left, right, LitEdgeAlpha * b);
                        else if (pass == 2 && b > 0f)
                            DrawGlow(node, b);
                        else if (pass == 3)
                            DrawNode(node, b, icons[h][n]);
                    }
            GUI.color = previous;
        }

        // Edges from a row of points below, spaced from the first node so neighbours share them,
        // and kept between the first and last node so the outer fans lean in
        private void DrawEdges(Rect node, float left, float right, float alpha)
        {
            float d = node.width;
            float spacing = d * EdgeSpacing;
            var to = new Vector2(node.center.x, node.yMax);
            float y = node.center.y + d * EdgeLength;
            float first = left + Mathf.Ceil(Mathf.Max(0f, to.x - d * EdgeReach - left) / spacing) * spacing;
            for (float x = first; x <= Mathf.Min(right, to.x + d * EdgeReach); x += spacing)
                DrawEdge(new Vector2(x, y), to, Mathf.Max(2.5f, d * 0.1f), alpha);
        }

        private void DrawEdge(Vector2 from, Vector2 to, float width, float alpha)
        {
            Vector2 delta = to - from;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2, delta.magnitude, width), edge);
            GUI.matrix = matrix;
        }

        private void DrawGlow(Rect node, float b)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.55f * b);
            GUI.DrawTexture(Centered(node, 2.4f), glow);
        }

        private void DrawNode(Rect node, float b, Texture2D icon)
        {
            float alpha = Mathf.Lerp(0.82f, 1f, b);
            Color fill = Color.Lerp(FillDark, Color.white, b);
            fill.a = alpha;
            GUI.color = fill;
            GUI.DrawTexture(node, disk);

            GUI.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.5f, 1f, b));
            GUI.DrawTexture(node, ring);

            // The icon is dark, so it only shows once the node lights up
            GUI.color = new Color(IconDark.r, IconDark.g, IconDark.b, alpha);
            GUI.DrawTexture(Centered(node, 0.62f), icon);
        }

        private static Rect Centered(Rect node, float scale)
        {
            float size = Mathf.Round(node.width * scale);
            return new Rect(Mathf.Round(node.center.x - size / 2), Mathf.Round(node.center.y - size / 2), size, size);
        }

        private static Texture2D NewTexture(int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };

        // White, with alpha a function of the distance from the centre (1 at the edge)
        private static Texture2D Radial(Func<float, float> alpha)
        {
            const int size = 128;
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x - 63.5f, y - 63.5f).magnitude / 64f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(r)) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        // A line fading in from its far end (left) to the node (right), with soft sides
        private static Texture2D EdgeTexture()
        {
            const int length = 64;
            var texture = NewTexture(length, 4);
            var pixels = new Color32[length * 4];
            for (int y = 1; y < 3; y++)
                for (int x = 0; x < length; x++)
                    pixels[y * length + x] = new Color32(255, 255, 255, (byte)(Mathf.Pow(x / (length - 1f), 1.5f) * 255));
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        // Called through reflection: LoadImage has a ReadOnlySpan overload, which net472 can't compile against
        private static readonly MethodInfo LoadImage =
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        private static Texture2D LoadIcon(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"SilksongRL.Icons.{name}.png"))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                var texture = NewTexture(2, 2);
                LoadImage.Invoke(null, new object[] { texture, bytes.ToArray() });
                return texture;
            }
        }
    }
}
