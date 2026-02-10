using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Captures the game screen, downsizes it, and converts to greyscale for visual observations.
    /// Supports cropping margins to exclude UI elements or focus on specific screen regions.
    /// </summary>
    public class ScreenCaptureOld
    {
        private readonly int targetWidth;
        private readonly int targetHeight;
        
        // Crop margins (in pixels, relative to current screen resolution)
        private int cropTop;
        private int cropBottom;
        private int cropLeft;
        private int cropRight;
        
        private Texture2D captureTexture;
        private Texture2D downsizedTexture;
        private RenderTexture downsizeRenderTexture;
        
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int croppedWidth;
        private int croppedHeight;
        
        private float[] cachedObservation;
        
        // Luminance coefficients for greyscale conversion
        private const float LUM_R = 0.2126f;
        private const float LUM_G = 0.7152f;
        private const float LUM_B = 0.0722f;

        /// <summary>
        /// Creates a new ScreenCapture instance.
        /// </summary>
        /// <param name="width">Target width for the downsized image</param>
        /// <param name="height">Target height for the downsized image</param>
        /// <param name="cropTop">Pixels to crop from top of screen</param>
        /// <param name="cropBottom">Pixels to crop from bottom of screen</param>
        /// <param name="cropLeft">Pixels to crop from left of screen</param>
        /// <param name="cropRight">Pixels to crop from right of screen</param>
        public ScreenCaptureOld(int width = 84, int height = 84, 
            int cropTop = 0, int cropBottom = 0, int cropLeft = 0, int cropRight = 0)
        {
            targetWidth = width;
            targetHeight = height;
            
            this.cropTop = cropTop;
            this.cropBottom = cropBottom;
            this.cropLeft = cropLeft;
            this.cropRight = cropRight;
            
            InitializeTextures();
        }

        /// <summary>
        /// Updates crop margins at runtime.
        /// Only to be used for testing with ScreenCaptureTest.
        /// </summary>
        public void SetCropMargins(int top, int bottom, int left, int right)
        {
            cropTop = top;
            cropBottom = bottom;
            cropLeft = left;
            cropRight = right;
            
            // Force texture recreation on next capture
            lastScreenWidth = 0;
            lastScreenHeight = 0;
        }

        private void InitializeTextures()
        {
            downsizedTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            downsizeRenderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            downsizeRenderTexture.filterMode = FilterMode.Bilinear;
        }

        /// <summary>
        /// Captures the current screen, downsizes it, and returns greyscale pixel values.
        /// Should be called at the end of a frame.
        /// </summary>
        /// <returns>Float array of greyscale values normalized to [0, 1], sized (width * height)</returns>
        public float[] CaptureGreyscale()
        {
            Texture2D croppedScreen = CaptureScreen();
            if (croppedScreen == null)
            {
                return null;
            }

            Texture2D downsized = DownsizeTexture(croppedScreen);

            return ToGreyscaleArray(downsized);
        }


        /// <summary>
        /// Captures the screen with cropping applied and returns the texture.
        /// </summary>
        private Texture2D CaptureScreen()
        {
            int screenW = Screen.width;
            int screenH = Screen.height;
            
            croppedWidth = screenW - cropLeft - cropRight;
            croppedHeight = screenH - cropTop - cropBottom;
            
            if (croppedWidth <= 0 || croppedHeight <= 0)
            {
                RLManager.StaticLogger?.LogError(
                    $"[ScreenCapture] Invalid crop: screen {screenW}x{screenH}, " +
                    $"crops T:{cropTop} B:{cropBottom} L:{cropLeft} R:{cropRight} " +
                    $"results in {croppedWidth}x{croppedHeight}");
                return null;
            }
            
            if (captureTexture == null || 
                lastScreenWidth != screenW || 
                lastScreenHeight != screenH)
            {
                if (captureTexture != null)
                {
                    Object.Destroy(captureTexture);
                }
                captureTexture = new Texture2D(croppedWidth, croppedHeight, TextureFormat.RGB24, false);
                lastScreenWidth = screenW;
                lastScreenHeight = screenH;
            }

            Rect captureRect = new Rect(cropLeft, cropBottom, croppedWidth, croppedHeight);
            captureTexture.ReadPixels(captureRect, 0, 0);
            captureTexture.Apply();

            return captureTexture;
        }

        /// <summary>
        /// Downsizes a texture to the target dimensions using GPU bilinear filtering.
        /// </summary>
        private Texture2D DownsizeTexture(Texture2D source)
        {
            // Use GPU to downsize with bilinear filtering
            RenderTexture previousActive = RenderTexture.active;
            
            // Blit source texture to the smaller render texture (GPU does the downsampling)
            Graphics.Blit(source, downsizeRenderTexture);
            
            RenderTexture.active = downsizeRenderTexture;
            downsizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            downsizedTexture.Apply();
            
            RenderTexture.active = previousActive;

            return downsizedTexture;
        }

        /// <summary>
        /// Converts an RGB texture to a greyscale float array using luminance formula.
        /// </summary>
        private float[] ToGreyscaleArray(Texture2D texture)
        {
            Color[] pixels = texture.GetPixels();
            float[] greyscale = new float[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                // Luminance formula (Rec. 709)
                greyscale[i] = pixels[i].r * LUM_R + pixels[i].g * LUM_G + pixels[i].b * LUM_B;
            }

            return greyscale;
        }

        /// <summary>
        /// Updates the cached observation. Call this at end of frame (after rendering).
        /// </summary>
        public void UpdateCache()
        {
            cachedObservation = CaptureGreyscale();
        }

        /// <summary>
        /// Gets the cached observation. Safe to call anytime.
        /// Returns null if no observation has been cached yet.
        /// </summary>
        public float[] GetCachedObservation()
        {
            return cachedObservation;
        }

        public void ClearCache()
        {
            cachedObservation = null;
        }
        
        /// <summary>
        /// Gets the current cropped capture dimensions (before downscaling).
        /// Returns (0, 0) if no capture has been performed yet.
        /// </summary>
        public (int width, int height) GetCroppedDimensions()
        {
            return (croppedWidth, croppedHeight);
        }

        /// <summary>
        /// Cleans up resources. Call this when done with the ScreenCapture instance.
        /// </summary>
        public void Dispose()
        {
            if (captureTexture != null)
            {
                Object.Destroy(captureTexture);
                captureTexture = null;
            }
            
            if (downsizedTexture != null)
            {
                Object.Destroy(downsizedTexture);
                downsizedTexture = null;
            }
            
            if (downsizeRenderTexture != null)
            {
                downsizeRenderTexture.Release();
                Object.Destroy(downsizeRenderTexture);
                downsizeRenderTexture = null;
            }
        }
    }
}
