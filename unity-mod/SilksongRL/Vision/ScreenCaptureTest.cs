using UnityEngine;
using System.Collections;
using System.IO;

namespace SilksongRL
{
    /// <summary>
    /// Test component for ScreenCapture functionality.
    /// Useful to figure out what to crop to get the best
    /// visual observations.
    /// 
    /// Controls:
    ///   F8: Capture and save screenshot
    ///   F9: Toggle live preview
    ///   F10: Toggle crop adjustment mode
    ///   
    /// In crop adjustment mode (with preview on):
    ///   Arrow keys: Adjust selected margin by 10px
    ///   Shift + Arrow: Adjust by 50px
    ///   Tab: Cycle through margins (Top → Bottom → Left → Right)
    /// 
    /// Add to RLManager with: gameObject.AddComponent&lt;ScreenCaptureTest&gt;();
    /// </summary>
    public class ScreenCaptureTest : MonoBehaviour
    {
        private ScreenCapture screenCapture;
        private Texture2D displayTexture;
        private bool showPreview = false;
        private bool cropAdjustMode = false;
        private string lastSavePath = "";
        private int captureCount = 0;
        
        private const int CAPTURE_WIDTH = 84;
        private const int CAPTURE_HEIGHT = 84;
        
        private int cropTop = 0;
        private int cropBottom = 0;
        private int cropLeft = 0;
        private int cropRight = 0;
        
        // Which margin is currently selected for adjustment
        private enum CropMargin { Top, Bottom, Left, Right }
        private CropMargin selectedMargin = CropMargin.Top;
        
        private const int PREVIEW_SCALE = 3;
        private Rect previewRect;
        
        private GUIStyle normalStyle;
        private GUIStyle selectedStyle;

        private void Awake()
        {
            RLManager.StaticLogger?.LogInfo("ScreenCapture Test loaded. F8=capture, F9=preview, F10=crop adjust");
            
            screenCapture = new ScreenCapture(CAPTURE_WIDTH, CAPTURE_HEIGHT, cropTop, cropBottom, cropLeft, cropRight);
            
            displayTexture = new Texture2D(CAPTURE_WIDTH, CAPTURE_HEIGHT, TextureFormat.RGB24, false);
            displayTexture.filterMode = FilterMode.Point;
            
            previewRect = new Rect(10, 10, CAPTURE_WIDTH * PREVIEW_SCALE, CAPTURE_HEIGHT * PREVIEW_SCALE);
        }

        private void Start()
        {
            Camera cam = Camera.main;

            Debug.Log($"[Debug] Camera: {cam.name}");
            Debug.Log($"[Debug] Camera.targetTexture: {cam.targetTexture?.name ?? "null (renders to screen)"}");
            Debug.Log($"[Debug] Camera.allowHDR: {cam.allowHDR}");
            Debug.Log($"[Debug] Camera.actualRenderingPath: {cam.actualRenderingPath}");
                
            Camera[] allCams = Object.FindObjectsOfType<Camera>();
            Debug.Log($"[Debug] Total cameras in scene: {allCams.Length}");
            foreach (var c in allCams)
            {
                Debug.Log($"  - {c.name} (depth: {c.depth}, culling: {c.cullingMask})");
            }
            
        }

        private void Update()
        {
            // F8: Capture and save to file
            if (Input.GetKeyDown(KeyCode.F8))
            {
                StartCoroutine(CaptureAndSave());
            }
            
            // F9: Toggle live preview
            if (Input.GetKeyDown(KeyCode.F9))
            {
                showPreview = !showPreview;
                RLManager.StaticLogger?.LogInfo($"Preview display: {(showPreview ? "ON" : "OFF")}");
            }
            
            // F10: Toggle crop adjustment mode
            if (Input.GetKeyDown(KeyCode.F10))
            {
                cropAdjustMode = !cropAdjustMode;
                RLManager.StaticLogger?.LogInfo($"Crop adjustment mode: {(cropAdjustMode ? "ON" : "OFF")}");
            }
            
            // Crop adjustment controls (only when preview is on and adjust mode is active)
            if (showPreview && cropAdjustMode)
            {
                HandleCropAdjustment();
            }
        }

        private void HandleCropAdjustment()
        {
            // Tab to cycle through margins
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                selectedMargin = (CropMargin)(((int)selectedMargin + 1) % 4);
                RLManager.StaticLogger?.LogInfo($"Selected margin: {selectedMargin}");
            }
            
            // Arrow keys to adjust (Shift for larger increments)
            int increment = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 50 : 10;
            
            bool changed = false;
            
            // Up/Down arrows adjust the value
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                AdjustMargin(increment);
                changed = true;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                AdjustMargin(-increment);
                changed = true;
            }
            
            if (changed)
            {
                cropTop = Mathf.Max(0, cropTop);
                cropBottom = Mathf.Max(0, cropBottom);
                cropLeft = Mathf.Max(0, cropLeft);
                cropRight = Mathf.Max(0, cropRight);
                
                screenCapture.SetCropMargins(cropTop, cropBottom, cropLeft, cropRight);
                
                RLManager.StaticLogger?.LogInfo($"Crop margins - T:{cropTop} B:{cropBottom} L:{cropLeft} R:{cropRight}");
            }
        }

        private void AdjustMargin(int delta)
        {
            switch (selectedMargin)
            {
                case CropMargin.Top:
                    cropTop += delta;
                    break;
                case CropMargin.Bottom:
                    cropBottom += delta;
                    break;
                case CropMargin.Left:
                    cropLeft += delta;
                    break;
                case CropMargin.Right:
                    cropRight += delta;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (showPreview)
            {
                StartCoroutine(UpdatePreview());
            }
        }

        private IEnumerator CaptureAndSave()
        {
            // Wait for end of frame to ensure rendering is complete
            yield return new WaitForEndOfFrame();
            
            float[] greyscaleData = screenCapture.CaptureGreyscale();
            
            if (greyscaleData == null)
            {
                RLManager.StaticLogger?.LogError("Failed to capture screen!");
                yield break;
            }
            
            Texture2D saveTexture = new Texture2D(CAPTURE_WIDTH, CAPTURE_HEIGHT, TextureFormat.RGB24, false);
            Color[] colors = new Color[greyscaleData.Length];
            
            for (int i = 0; i < greyscaleData.Length; i++)
            {
                float g = greyscaleData[i];
                colors[i] = new Color(g, g, g);
            }
            
            saveTexture.SetPixels(colors);
            saveTexture.Apply();
            
            string savePath = GetSavePath();
            byte[] pngData = saveTexture.EncodeToPNG();
            File.WriteAllBytes(savePath, pngData);
            
            // Also save the original (non-greyscale) downsized for comparison
            string colorPath = savePath.Replace(".png", "_color.png");
            SaveColorCapture(colorPath);
            
            lastSavePath = savePath;
            captureCount++;
            
            var (croppedW, croppedH) = screenCapture.GetCroppedDimensions();
            
            RLManager.StaticLogger?.LogInfo($"Saved greyscale capture to: {savePath}");
            RLManager.StaticLogger?.LogInfo($"Saved color capture to: {colorPath}");
            RLManager.StaticLogger?.LogInfo($"Cropped region: {croppedW}x{croppedH} -> Output: {CAPTURE_WIDTH}x{CAPTURE_HEIGHT}");
            RLManager.StaticLogger?.LogInfo($"Crop margins: T:{cropTop} B:{cropBottom} L:{cropLeft} R:{cropRight}");
            
            Object.Destroy(saveTexture);
        }

        private void SaveColorCapture(string path)
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            int croppedW = screenW - cropLeft - cropRight;
            int croppedH = screenH - cropTop - cropBottom;
            
            if (croppedW <= 0 || croppedH <= 0) return;
            
            Texture2D croppedScreen = new Texture2D(croppedW, croppedH, TextureFormat.RGB24, false);
            croppedScreen.ReadPixels(new Rect(cropLeft, cropBottom, croppedW, croppedH), 0, 0);
            croppedScreen.Apply();
            
            RenderTexture rt = new RenderTexture(CAPTURE_WIDTH, CAPTURE_HEIGHT, 0);
            rt.filterMode = FilterMode.Bilinear;
            
            Graphics.Blit(croppedScreen, rt);
            
            RenderTexture.active = rt;
            Texture2D downsized = new Texture2D(CAPTURE_WIDTH, CAPTURE_HEIGHT, TextureFormat.RGB24, false);
            downsized.ReadPixels(new Rect(0, 0, CAPTURE_WIDTH, CAPTURE_HEIGHT), 0, 0);
            downsized.Apply();
            RenderTexture.active = null;
            
            byte[] pngData = downsized.EncodeToPNG();
            File.WriteAllBytes(path, pngData);
            
            Object.Destroy(croppedScreen);
            Object.Destroy(downsized);
            rt.Release();
            Object.Destroy(rt);
        }

        private IEnumerator UpdatePreview()
        {
            yield return new WaitForEndOfFrame();
            
            float[] greyscaleData = screenCapture.CaptureGreyscale();
            if (greyscaleData == null) yield break;
            
            Color[] colors = new Color[greyscaleData.Length];
            for (int i = 0; i < greyscaleData.Length; i++)
            {
                float g = greyscaleData[i];
                colors[i] = new Color(g, g, g);
            }
            
            displayTexture.SetPixels(colors);
            displayTexture.Apply();
        }

        private void OnGUI()
        {
            if (normalStyle == null)
            {
                normalStyle = new GUIStyle(GUI.skin.label);
                normalStyle.normal.textColor = Color.white;
                
                selectedStyle = new GUIStyle(GUI.skin.label);
                selectedStyle.normal.textColor = Color.yellow;
            }
            
            if (showPreview && displayTexture != null)
            {
                GUI.DrawTexture(new Rect(previewRect.x - 2, previewRect.y - 2, 
                    previewRect.width + 4, previewRect.height + 4), Texture2D.blackTexture);
                
                GUI.DrawTexture(previewRect, displayTexture);
                
                float infoY = previewRect.y + previewRect.height + 5;
                GUI.Label(new Rect(previewRect.x, infoY, 300, 20), 
                    $"Output: {CAPTURE_WIDTH}x{CAPTURE_HEIGHT}", normalStyle);
                
                if (cropAdjustMode)
                {
                    float cropInfoY = infoY + 20;
                    GUI.Label(new Rect(previewRect.x, cropInfoY, 300, 20), 
                        "Crop Margins (Tab to select, ↑↓ to adjust):", normalStyle);
                    
                    cropInfoY += 18;
                    DrawMarginLabel(previewRect.x, cropInfoY, "Top", cropTop, CropMargin.Top);
                    DrawMarginLabel(previewRect.x + 80, cropInfoY, "Bottom", cropBottom, CropMargin.Bottom);
                    DrawMarginLabel(previewRect.x + 175, cropInfoY, "Left", cropLeft, CropMargin.Left);
                    DrawMarginLabel(previewRect.x + 255, cropInfoY, "Right", cropRight, CropMargin.Right);
                }
            }
            
            string helpText = "F8: Save | F9: Preview | F10: Crop Adjust";
            if (cropAdjustMode)
            {
                helpText += " [ACTIVE - Tab/↑↓/Shift]";
            }
            if (!string.IsNullOrEmpty(lastSavePath))
            {
                helpText += $"\nLast: {Path.GetFileName(lastSavePath)}";
            }
            
            GUI.Label(new Rect(10, Screen.height - 50, 500, 45), helpText, normalStyle);
        }

        private void DrawMarginLabel(float x, float y, string name, int value, CropMargin margin)
        {
            GUIStyle style = (selectedMargin == margin && cropAdjustMode) ? selectedStyle : normalStyle;
            GUI.Label(new Rect(x, y, 80, 20), $"{name}: {value}", style);
        }

        private string GetSavePath()
        {
            // Save to a 'captures' folder next to the game executable
            string capturesDir = Path.Combine(BepInEx.Paths.GameRootPath, "captures");
            
            if (!Directory.Exists(capturesDir))
            {
                Directory.CreateDirectory(capturesDir);
            }
            
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(capturesDir, $"capture_{timestamp}_{captureCount:D3}.png");
        }

        private void OnDestroy()
        {
            screenCapture?.Dispose();
            
            if (displayTexture != null)
            {
                Object.Destroy(displayTexture);
            }
        }
    }
}
