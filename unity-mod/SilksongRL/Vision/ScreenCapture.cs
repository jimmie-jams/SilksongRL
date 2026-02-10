using UnityEngine;
using UnityEngine.Rendering;


namespace SilksongRL
{
    /// <summary>
    /// Captures the game screen, downsizes it, and converts to greyscale for visual observations.
    /// Excludes UI elements entirely. Supports cropping margins to focus on specific screen regions.
    /// Slightly less performant than ScreenCaptureOld but no UI is worth the trade-off, I believe.
    /// Note that unlike the previous implementation, this one latches onto the main camera so the 
    /// camera MUST have loaded before trying to init this.
    /// </summary>
    public class ScreenCapture
    {
        private RenderTexture preUIRenderTexture;
        private RenderTexture downsizeRenderTexture;
        private Texture2D downsizedTexture;
        private CommandBuffer captureCommand;
        private Camera gameCamera;
        
        private int targetWidth;
        private int targetHeight;
        
        private int cropTop;
        private int cropBottom;
        private int cropLeft;
        private int cropRight;
        
        private int lastScreenWidth;
        private int lastScreenHeight;
        
        private float[] cachedObservation;
        
        private const float LUM_R = 0.2126f;
        private const float LUM_G = 0.7152f;
        private const float LUM_B = 0.0722f;


        public ScreenCapture(int width = 84, int height = 84, int cropT = 0, int cropB = 0, int cropL = 0, int cropR = 0)
        {
            targetWidth = width;
            targetHeight = height;
            cropTop = cropT;
            cropBottom = cropB;
            cropLeft = cropL;
            cropRight = cropR;
            
            Initialize();
        }

        private void Initialize()
        {
            gameCamera = Camera.main;
            if (gameCamera == null)
            {
                RLManager.StaticLogger?.LogError("[ScreenCapture] No main camera found!");
                return;
            }
            
            downsizedTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            downsizeRenderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            downsizeRenderTexture.filterMode = FilterMode.Bilinear;
            
            CreateScreenRenderTexture(Screen.width, Screen.height);
            
            SetupCommandBuffer();
        }

        private void CreateScreenRenderTexture(int width, int height)
        {
            if (preUIRenderTexture != null)
            {
                preUIRenderTexture.Release();
                Object.Destroy(preUIRenderTexture);
            }
            
            preUIRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            lastScreenWidth = width;
            lastScreenHeight = height;
        }

        private void SetupCommandBuffer()
        {
            if (gameCamera == null) return;
            
            if (captureCommand != null)
            {
                gameCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, captureCommand);
                captureCommand.Dispose();
            }
            
            captureCommand = new CommandBuffer { name = "PreUICapture" };
            captureCommand.Blit(BuiltinRenderTextureType.CameraTarget, preUIRenderTexture);
            gameCamera.AddCommandBuffer(CameraEvent.AfterEverything, captureCommand);
        }

        /// <summary>
        /// Update crop margins at runtime. Takes effect on next capture.
        /// </summary>
        public void SetCropMargins(int top, int bottom, int left, int right)
        {
            cropTop = top;
            cropBottom = bottom;
            cropLeft = left;
            cropRight = right;
        }

        /// <summary>
        /// Captures greyscale observation. Call after rendering (e.g., in coroutine after WaitForEndOfFrame).
        /// </summary>
        public float[] CaptureGreyscale()
        {
            if (gameCamera == null || preUIRenderTexture == null) return null;
            
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                CreateScreenRenderTexture(Screen.width, Screen.height);
                SetupCommandBuffer();
            }
            
            int screenW = Screen.width;
            int screenH = Screen.height;
            int croppedWidth = screenW - cropLeft - cropRight;
            int croppedHeight = screenH - cropTop - cropBottom;
            
            if (croppedWidth <= 0 || croppedHeight <= 0)
            {
                RLManager.StaticLogger?.LogError($"[ScreenCapture] Invalid crop dimensions: {croppedWidth}x{croppedHeight}");
                return null;
            }
            
            RenderTexture previous = RenderTexture.active;
            
            RenderTexture.active = preUIRenderTexture;
            
            Rect cropRect = new Rect(cropLeft, cropBottom, croppedWidth, croppedHeight);
            
            Texture2D tempCropped = new Texture2D(croppedWidth, croppedHeight, TextureFormat.RGB24, false);
            tempCropped.ReadPixels(cropRect, 0, 0);
            tempCropped.Apply();
            
            Graphics.Blit(tempCropped, downsizeRenderTexture);
            
            RenderTexture.active = downsizeRenderTexture;
            downsizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            downsizedTexture.Apply();
            
            RenderTexture.active = previous;
            
            Object.Destroy(tempCropped);
            
            return ToGreyscaleArray(downsizedTexture);
        }

        private float[] ToGreyscaleArray(Texture2D texture)
        {
            Color[] pixels = texture.GetPixels();
            float[] greyscale = new float[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                greyscale[i] = pixels[i].r * LUM_R + pixels[i].g * LUM_G + pixels[i].b * LUM_B;
            }

            return greyscale;
        }

        public (int width, int height) GetCroppedDimensions()
        {
            return (Screen.width - cropLeft - cropRight, Screen.height - cropTop - cropBottom);
        }

        public void UpdateCache()
        {
            cachedObservation = CaptureGreyscale();
        }

        public float[] GetCachedObservation()
        {
            return cachedObservation;
        }

        public void ClearCache()
        {
            cachedObservation = null;
        }

        public void Dispose()
        {
            if (gameCamera != null && captureCommand != null)
            {
                gameCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, captureCommand);
            }
            
            captureCommand?.Dispose();
            
            if (preUIRenderTexture != null)
            {
                preUIRenderTexture.Release();
                Object.Destroy(preUIRenderTexture);
            }
            
            if (downsizeRenderTexture != null)
            {
                downsizeRenderTexture.Release();
                Object.Destroy(downsizeRenderTexture);
            }
            
            if (downsizedTexture != null)
            {
                Object.Destroy(downsizedTexture);
            }
        }
    }
}