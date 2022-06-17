﻿using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Multi_Channel_Image_Tool.Additional_Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using Path = System.IO.Path;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Multi_Channel_Image_Tool
{
    public static class ImageUtility
    {
        public static class EditorImages
        {
            public static ImageSource GetImageFromFolder(string imagePath) => new BitmapImage(new Uri($"pack://application:,,,{imagePath}", UriKind.RelativeOrAbsolute));
            public static ImageSource Error => GetImageFromFolder("/Images/Error.png");
            public static ImageSource Icon => GetImageFromFolder("../Icon.ico");
        }

        public static class Validation
        {
            private const string _PNG = ".png";
            private static readonly string[] _VALID_EXTENSIONS = new[] { ".jpg", ".jpeg", _PNG };
            public const string _VALID_EXTENSIONS_AS_STRING_LIST = "png, jpg, jpeg";

            private static bool IsValidBitmap(string filename)
            {
                try
                {
                    using (var bmp = new Bitmap(filename)) { }
                    return true;
                }
                catch (Exception) { return false; }
            }
            public static bool IsValidImage(string imagePath) => File.Exists(imagePath) && _VALID_EXTENSIONS.Contains(Path.GetExtension(imagePath)) && IsValidBitmap(imagePath);
            public static bool ImageHasTransparency(string imagePath) => Path.GetExtension(imagePath).Equals(_PNG);
        }

        public static class ImageGeneration
        {
            #region Internal

            [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject([In] IntPtr hObject);

            #endregion Internal

            #region Converters

            public static ImageSource BitmapToImageSource(Bitmap bmp)
            {
                var handle = bmp.GetHbitmap();
                try
                {
                    return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                finally { DeleteObject(handle); }
            }

            #endregion Converters

            public static Bitmap GenerateSolidColor(int r, int g, int b, int a)
            {
                Bitmap solidColor = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
                solidColor.SetPixel(0, 0, Color.FromArgb(a, r, g, b));

                return solidColor;
            }

            public static ImageSource ExtractChannelAndGetSource(string imagePath, EChannel channelToExtract, EChannel finalChannel, bool invert)
            => BitmapToImageSource(ExtractChannel(imagePath, channelToExtract, finalChannel, invert));

            public static unsafe Bitmap ExtractChannel(string imagePath, EChannel channelToExtract, EChannel finalChannel, bool invert, string popupTextExtra = "")
            {
                if (!ImageUtility.Validation.IsValidImage(imagePath)) { return new Bitmap(1, 1); }

                Bitmap image = new Bitmap(imagePath);
                BitmapData imageData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

                PopupTextWindow.OpenWindowAndExecute($"Generating Texture By Extracting Channel, Please Wait {popupTextExtra}",
                    () =>
                    {
                        byte* startPos = (byte*)imageData.Scan0.ToPointer();
                        int stride = imageData.Stride;
                        bool imageHasTransparency = ImageUtility.Validation.ImageHasTransparency(imagePath);

                        for (int y = 0; y < imageData.Height; y++)
                        {
                            byte* row = startPos + (y * stride);

                            for (int x = 0; x < imageData.Width; x++)
                            {
                                int bIndex = x * 4; // 4 = 4 bytes per pixel
                                int gIndex = bIndex + 1;
                                int rIndex = bIndex + 2;
                                int aIndex = bIndex + 3;

                                switch (channelToExtract)
                                {
                                    case EChannel.R:
                                        byte pixelR = row[rIndex];
                                        if (invert) { pixelR = (byte)(byte.MaxValue - pixelR); }

                                        row[rIndex] = pixelR;
                                        row[gIndex] = byte.MinValue;
                                        row[bIndex] = byte.MinValue;
                                        break;
                                    case EChannel.G:
                                        byte pixelG = row[gIndex];
                                        if (invert) { pixelG = (byte)(byte.MaxValue - pixelG); }

                                        row[rIndex] = byte.MinValue;
                                        row[gIndex] = pixelG;
                                        row[bIndex] = byte.MinValue;
                                        break;
                                    case EChannel.B:
                                        byte pixelB = row[bIndex];
                                        if (invert) { pixelB = (byte)(byte.MaxValue - pixelB); }

                                        row[rIndex] = byte.MinValue;
                                        row[gIndex] = byte.MinValue;
                                        row[bIndex] = pixelB;
                                        break;
                                    case EChannel.A:
                                        byte pixelA = imageHasTransparency ? row[aIndex] : byte.MaxValue;
                                        if (invert) { pixelA = (byte)(byte.MaxValue - pixelA); }

                                        row[rIndex] = pixelA;
                                        row[gIndex] = pixelA;
                                        row[bIndex] = pixelA;
                                        row[aIndex] = byte.MaxValue;
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException(nameof(channelToExtract), channelToExtract, null);
                                }
                            }
                        }
                    });

                image.UnlockBits(imageData);
                return image;
            }

            public static ImageSource CombineChannelsAndGetSource(Bitmap r, Bitmap g, Bitmap b, Bitmap a)
                => BitmapToImageSource(CombineChannels(r, g, b, a));

            public static Bitmap CombineChannels(Bitmap r, Bitmap g, Bitmap b, Bitmap a)
            {
                int width = Helpers.Max(r.Width, g.Width, b.Width, a.Width);
                int height = Helpers.Max(r.Height, g.Height, b.Height, a.Height);

                var merged = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                PopupTextWindow popupText = new PopupTextWindow
                {
                    PopupText = { Text = "Creating Texture, Please Wait" }
                };
                popupText.Show();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int rIntensity = r.GetPixel(x % r.Width, y % r.Height).R;
                        int gIntensity = g.GetPixel(x % g.Width, y % g.Height).G;
                        int bIntensity = b.GetPixel(x % b.Width, y % b.Height).B;
                        // This uses the generated previews, so the alpha preview has 255 alpha throughout. However, any other channel has the alpha value.
                        int aIntensity = a.GetPixel(x % a.Width, y % a.Height).R;

                        merged.SetPixel(x, y, Color.FromArgb(aIntensity, rIntensity, gIntensity, bIntensity));
                    }
                }

                popupText.Close();
                return merged;
            }
        }
    }
}
